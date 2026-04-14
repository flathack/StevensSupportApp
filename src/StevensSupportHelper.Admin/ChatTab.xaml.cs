using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin;

public partial class ChatTab : UserControl
{
    private readonly Models.ClientRow _client;
    private readonly AdminApiClient _apiClient;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private readonly string? _mfaCode;
    private readonly ObservableCollection<ChatMessageItem> _messages = [];
    private readonly DispatcherTimer _refreshTimer;
    private bool _isBusy;

    public ChatTab(Models.ClientRow client, AdminApiClient apiClient, string serverUrl, string apiKey, string? mfaCode)
    {
        _client = client;
        _apiClient = apiClient;
        _serverUrl = serverUrl;
        _apiKey = apiKey;
        _mfaCode = mfaCode;
        InitializeComponent();
        MessagesListView.ItemsSource = _messages;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshMessagesAsync(markViewed: false);
        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshMessagesAsync();
        };
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    public event EventHandler<DateTimeOffset?>? MessagesViewed;

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshMessagesAsync();

    private async void SendButton_OnClick(object sender, RoutedEventArgs e)
    {
        var text = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusTextBlock.Text = "Enter a message first.";
            return;
        }

        try
        {
            ToggleBusy(true, $"Sending message to {_client.DeviceName}...");
            await _apiClient.SendChatMessageAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, text, CancellationToken.None);
            MessageTextBox.Text = string.Empty;
            await RefreshMessagesAsync();
            StatusTextBlock.Text = $"Message sent to {_client.DeviceName}.";
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Sending message failed: {exception.Message}");
        }
    }

    private async Task RefreshMessagesAsync(bool markViewed = true)
    {
        try
        {
            ToggleBusy(true, $"Loading chat with {_client.DeviceName}...");
            var messages = await _apiClient.GetChatMessagesAsync(_serverUrl, _apiKey, _mfaCode, _client.ClientId, CancellationToken.None);
            _messages.Clear();
            foreach (var message in messages.OrderByDescending(item => item.CreatedAtUtc))
            {
                _messages.Add(new ChatMessageItem(
                    message.CreatedAtUtc,
                    $"{message.SenderRole}: {message.SenderDisplayName}",
                    message.Message));
            }

            var latestClientMessageAtUtc = messages
                .Where(static message => string.Equals(message.SenderRole, "Client", StringComparison.OrdinalIgnoreCase))
                .Select(static message => (DateTimeOffset?)message.CreatedAtUtc)
                .LastOrDefault();
            if (markViewed)
            {
                MessagesViewed?.Invoke(this, latestClientMessageAtUtc);
            }

            ToggleBusy(false, $"Loaded {_messages.Count} chat messages.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Loading chat failed: {exception.Message}");
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        SendButton.IsEnabled = !isBusy;
        MessageTextBox.IsEnabled = !isBusy;
        StatusTextBlock.Text = status;
    }

    private sealed record ChatMessageItem(DateTimeOffset CreatedAtUtc, string Direction, string Text);
}
