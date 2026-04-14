using System.Drawing;
using System.Net;
using System.Windows.Forms;
using StevensSupportHelper.Client.Tray.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Client.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int FastPollingIntervalMs = 2000;
    private const int IdlePollingIntervalMs = 10000;
    private const int FailurePollingIntervalMs = 15000;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TrayApiClient _apiClient = new();
    private readonly ClientIdentityStore _identityStore = new();
    private readonly TrayOptions _options = new();
    private readonly PendingUpdateStore _pendingUpdateStore;
    private SupportApprovalForm? _approvalForm;
    private ClientChatForm? _chatForm;
    private Guid? _lastPromptedRequestId;
    private Guid? _lastSeenChatMessageId;
    private ClientIdentity? _identity;
    private bool _isPolling;
    private string? _lastServerError;
    private string? _lastNotifiedUpdateVersion;

    public TrayApplicationContext()
    {
        _pendingUpdateStore = new PendingUpdateStore(_options);
        var applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        _notifyIcon = new NotifyIcon
        {
            Icon = applicationIcon,
            Text = "StevensSupportHelper Client",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += async (_, _) => await ShowStatusAsync();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = IdlePollingIntervalMs
        };
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();

        _ = PollAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _approvalForm?.Dispose();
            _chatForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status pruefen", null, async (_, _) => await ShowStatusAsync());
        menu.Items.Add("Chat oeffnen", null, async (_, _) => await OpenChatAsync());
        menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        return menu;
    }

    private async Task ShowStatusAsync()
    {
        var state = await PollAsync(forcePrompt: false);
        var message = state?.ActiveSession is { } session
            ? $"Aktive Session mit {session.AdminDisplayName} via {session.Channel}."
            : state?.PendingSupportRequest is { } request
                ? $"Ausstehende Anfrage von {request.AdminDisplayName} via {request.PreferredChannel}."
                : "Keine aktive Support-Sitzung und keine offene Anfrage.";
        var pendingUpdate = _pendingUpdateStore.Load();
        if (pendingUpdate is not null)
        {
            message += $" Update {pendingUpdate.Version} ist bereitgestellt.";
        }

        if (state?.ChatMessages.Count > 0)
        {
            message += $" Chat: {state.ChatMessages.Count} Nachrichten.";
        }

        _notifyIcon.ShowBalloonTip(3000, "StevensSupportHelper", message, ToolTipIcon.Info);
    }

    private async Task<GetSupportStateResponse?> PollAsync(bool forcePrompt = true)
    {
        if (_isPolling)
        {
            return null;
        }

        _isPolling = true;
        try
        {
            _identity ??= await _identityStore.LoadAsync(CancellationToken.None);
            if (_identity is null)
            {
                _timer.Interval = IdlePollingIntervalMs;
                _notifyIcon.Text = "StevensSupportHelper Client - wartet auf Registrierung";
                return null;
            }

            var state = await _apiClient.GetSupportStateAsync(_options.ServerBaseUrl, _identity, CancellationToken.None);
            _lastServerError = null;
            _timer.Interval = state.ActiveSession is not null || state.PendingSupportRequest is not null
                ? FastPollingIntervalMs
                : IdlePollingIntervalMs;
            UpdateTooltip(state);
            NotifyPendingUpdateIfNeeded();

            if (forcePrompt && state.PendingSupportRequest is { } request && request.RequestId != _lastPromptedRequestId)
            {
                _lastPromptedRequestId = request.RequestId;
                ShowApprovalForm(request);
            }

            HandleChatMessages(state.ChatMessages);
            if (_chatForm is not null && !_chatForm.IsDisposed)
            {
                _chatForm.SetMessages(state.ChatMessages);
                _chatForm.SetBusy(false, $"Loaded {state.ChatMessages.Count} chat messages.");
            }

            return state;
        }
        catch (Exception exception)
        {
            if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
            {
                _identity = null;
                await _identityStore.DeleteAsync(CancellationToken.None);
                _timer.Interval = FailurePollingIntervalMs;
                _notifyIcon.Text = "StevensSupportHelper Client - wartet auf Registrierung";
                return null;
            }

            _timer.Interval = FailurePollingIntervalMs;
            _notifyIcon.Text = "StevensSupportHelper Client - Server nicht erreichbar";
            if (!string.Equals(_lastServerError, exception.Message, StringComparison.Ordinal))
            {
                _lastServerError = exception.Message;
                _notifyIcon.ShowBalloonTip(3000, "StevensSupportHelper", exception.Message, ToolTipIcon.Warning);
            }

            return null;
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void UpdateTooltip(GetSupportStateResponse state)
    {
        var pendingUpdate = _pendingUpdateStore.Load();
        var text = state.ActiveSession is not null
            ? $"Aktive Session: {state.ActiveSession.AdminDisplayName}"
            : state.PendingSupportRequest is not null
                ? $"Anfrage offen: {state.PendingSupportRequest.AdminDisplayName}"
                : state.ChatMessages.Count > 0
                    ? $"Chat: {state.ChatMessages.Count} Nachrichten"
                    : pendingUpdate is not null
                        ? $"Update {pendingUpdate.Version} bereit"
                        : "Bereit";
        _notifyIcon.Text = $"StevensSupportHelper Client - {TrimTooltip(text)}";
    }

    private void NotifyPendingUpdateIfNeeded()
    {
        var pendingUpdate = _pendingUpdateStore.Load();
        if (pendingUpdate is null || string.Equals(_lastNotifiedUpdateVersion, pendingUpdate.Version, StringComparison.Ordinal))
        {
            return;
        }

        _lastNotifiedUpdateVersion = pendingUpdate.Version;
        _notifyIcon.ShowBalloonTip(
            5000,
            "Update bereit",
            $"Version {pendingUpdate.Version} wurde vorbereitet und liegt unter {pendingUpdate.PackagePath}.",
            ToolTipIcon.Info);
    }

    private void ShowApprovalForm(SupportRequestDto request)
    {
        _notifyIcon.ShowBalloonTip(
            5000,
            "Support-Anfrage",
            $"{request.AdminDisplayName} moechte sich per {request.PreferredChannel} verbinden.",
            ToolTipIcon.Info);

        _approvalForm?.Close();
        _approvalForm = new SupportApprovalForm(request);
        _approvalForm.ApproveRequested += async (_, _) => await SubmitDecisionAsync(request, approved: true);
        _approvalForm.DenyRequested += async (_, _) => await SubmitDecisionAsync(request, approved: false);
        _approvalForm.Show();
        _approvalForm.Activate();
    }

    private async Task SubmitDecisionAsync(SupportRequestDto request, bool approved)
    {
        if (_identity is null)
        {
            return;
        }

        try
        {
            var result = await _apiClient.SubmitDecisionAsync(
                _options.ServerBaseUrl,
                request.RequestId,
                new SubmitSupportDecisionRequest(_identity.ClientId, _identity.ClientSecret, approved),
                CancellationToken.None);

            _approvalForm?.Close();
            _approvalForm = null;

            var message = approved
                ? $"Anfrage genehmigt. Session-Status: {result.ActiveSession?.Status ?? result.SupportRequest.Status}."
                : "Anfrage abgelehnt.";
            _notifyIcon.ShowBalloonTip(4000, "StevensSupportHelper", message, ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            _notifyIcon.ShowBalloonTip(4000, "StevensSupportHelper", exception.Message, ToolTipIcon.Error);
        }
    }

    private void HandleChatMessages(IReadOnlyList<ChatMessageDto> messages)
    {
        var latest = messages.OrderBy(static item => item.CreatedAtUtc).LastOrDefault();
        if (latest is null)
        {
            return;
        }

        if (_lastSeenChatMessageId is null)
        {
            _lastSeenChatMessageId = latest.MessageId;
            return;
        }

        var previousMessage = messages.FirstOrDefault(item => item.MessageId == _lastSeenChatMessageId.Value);
        var previousTimestamp = previousMessage?.CreatedAtUtc ?? DateTimeOffset.MinValue;
        var newAdminMessages = messages
            .Where(message => message.SenderRole == "Admin" && message.CreatedAtUtc >= previousTimestamp)
            .Where(message => message.MessageId != _lastSeenChatMessageId)
            .OrderBy(message => message.CreatedAtUtc)
            .ToArray();
        if (newAdminMessages.Length > 0)
        {
            var newest = newAdminMessages[^1];
            _notifyIcon.ShowBalloonTip(5000, $"Nachricht von {newest.SenderDisplayName}", newest.Message, ToolTipIcon.Info);
        }

        _lastSeenChatMessageId = latest.MessageId;
    }

    private async Task OpenChatAsync()
    {
        _identity ??= await _identityStore.LoadAsync(CancellationToken.None);
        if (_identity is null)
        {
            _notifyIcon.ShowBalloonTip(3000, "StevensSupportHelper", "Client ist noch nicht registriert.", ToolTipIcon.Warning);
            return;
        }

        _chatForm ??= new ClientChatForm();
        _chatForm.SendRequested -= ChatForm_OnSendRequested;
        _chatForm.RefreshRequested -= ChatForm_OnRefreshRequested;
        _chatForm.SendRequested += ChatForm_OnSendRequested;
        _chatForm.RefreshRequested += ChatForm_OnRefreshRequested;
        _chatForm.Show();
        _chatForm.Activate();
        _chatForm.SetBusy(true, "Loading chat...");

        var state = await PollAsync(forcePrompt: false);
        _chatForm.SetMessages(state?.ChatMessages ?? []);
        _chatForm.SetBusy(false, $"Loaded {state?.ChatMessages.Count ?? 0} chat messages.");
    }

    private async void ChatForm_OnRefreshRequested(object? sender, EventArgs e)
    {
        if (_chatForm is null || _chatForm.IsDisposed)
        {
            return;
        }

        _chatForm.SetBusy(true, "Refreshing chat...");
        var state = await PollAsync(forcePrompt: false);
        _chatForm.SetMessages(state?.ChatMessages ?? []);
        _chatForm.SetBusy(false, $"Loaded {state?.ChatMessages.Count ?? 0} chat messages.");
    }

    private async void ChatForm_OnSendRequested(object? sender, EventArgs e)
    {
        if (_chatForm is null || _chatForm.IsDisposed)
        {
            return;
        }

        _identity ??= await _identityStore.LoadAsync(CancellationToken.None);
        if (_identity is null)
        {
            _chatForm.SetBusy(false, "Client identity is unavailable.");
            return;
        }

        var text = _chatForm.DraftMessage.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _chatForm.SetBusy(false, "Bitte zuerst eine Nachricht eingeben.");
            return;
        }

        try
        {
            _chatForm.SetBusy(true, "Sending message...");
            await _apiClient.SendChatMessageAsync(
                _options.ServerBaseUrl,
                new SendClientChatMessageRequest(_identity.ClientId, _identity.ClientSecret, text, Environment.UserName),
                CancellationToken.None);
            _chatForm.DraftMessage = string.Empty;
            var state = await PollAsync(forcePrompt: false);
            _chatForm.SetMessages(state?.ChatMessages ?? []);
            _chatForm.SetBusy(false, "Message sent.");
        }
        catch (Exception exception)
        {
            _chatForm.SetBusy(false, $"Sending failed: {exception.Message}");
        }
    }

    private static string TrimTooltip(string value)
    {
        return value.Length > 40 ? value[..40] : value;
    }
}
