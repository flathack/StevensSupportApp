using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StevensSupportHelper.Shared.Contracts;

namespace StevensSupportHelper.Admin.Models;

public sealed class ClientRow : INotifyPropertyChanged
{
    private static readonly TimeSpan OnlineThreshold = TimeSpan.FromSeconds(45);
    private Guid _clientId;
    private string _deviceName = string.Empty;
    private string _machineName = string.Empty;
    private string _currentUser = string.Empty;
    private bool _hasInteractiveUser;
    private bool _isAtLogonScreen;
    private string _agentVersion = "0.0.0.0";
    private int? _batteryPercentage;
    private bool _isOnline;
    private bool _consentRequired;
    private bool _autoApproveSupportRequests;
    private bool _tailscaleConnected;
    private string _tailscaleIpAddressesText = string.Empty;
    private string _rustDeskId = string.Empty;
    private string _rustDeskPassword = string.Empty;
    private string _remoteUserName = string.Empty;
    private string _remotePassword = string.Empty;
    private string _notes = string.Empty;
    private string _supportedChannelsText = string.Empty;
    private DateTimeOffset _lastSeenAtUtc;
    private string _pendingSupportRequestText = string.Empty;
    private string _activeSessionText = string.Empty;
    private string _activeSessionStatus = string.Empty;
    private string _availableUpdateVersion = string.Empty;
    private bool _isUpdateAvailable;
    private int _unreadClientChatCount;
    private DateTimeOffset? _lastClientChatMessageAtUtc;
    private bool _chatViewedInSession;
    private RemoteChannel? _activeChannel;
    private IReadOnlyList<ClientDiskUsageRow> _diskUsages = [];
    private long? _totalMemoryBytes;
    private long? _availableMemoryBytes;
    private string _osDescription = string.Empty;
    private DateTimeOffset? _lastBootAtUtc;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid ClientId { get => _clientId; set => SetField(ref _clientId, value); }
    public string DeviceName { get => _deviceName; set => SetField(ref _deviceName, value); }
    public string MachineName { get => _machineName; set => SetField(ref _machineName, value); }
    public string CurrentUser { get => _currentUser; set => SetFieldAndNotify(ref _currentUser, value, [nameof(ConnectionSummary), nameof(IsDirectAdminAccessAvailable), nameof(LogonStateSummary)]); }
    public bool HasInteractiveUser { get => _hasInteractiveUser; set => SetFieldAndNotify(ref _hasInteractiveUser, value, [nameof(ConnectionSummary), nameof(IsDirectAdminAccessAvailable), nameof(LogonStateSummary)]); }
    public bool IsAtLogonScreen { get => _isAtLogonScreen; set => SetFieldAndNotify(ref _isAtLogonScreen, value, [nameof(ConnectionSummary), nameof(IsDirectAdminAccessAvailable), nameof(LogonStateSummary)]); }
    public string AgentVersion { get => _agentVersion; set => SetField(ref _agentVersion, value); }
    public int? BatteryPercentage { get => _batteryPercentage; set => SetFieldAndNotify(ref _batteryPercentage, value, [nameof(BatteryText), nameof(BatteryBadgeText), nameof(HasBatteryInfo), nameof(ConnectionSummary)]); }
    public bool IsOnline => _isOnline;
    public bool ConsentRequired { get => _consentRequired; set => SetField(ref _consentRequired, value); }
    public bool AutoApproveSupportRequests { get => _autoApproveSupportRequests; set => SetField(ref _autoApproveSupportRequests, value); }
    public bool TailscaleConnected { get => _tailscaleConnected; set => SetField(ref _tailscaleConnected, value); }
    public string TailscaleIpAddressesText { get => _tailscaleIpAddressesText; set => SetField(ref _tailscaleIpAddressesText, value); }
    public string RustDeskId { get => _rustDeskId; set => SetField(ref _rustDeskId, value); }
    public string RustDeskPassword { get => _rustDeskPassword; set => SetField(ref _rustDeskPassword, value); }
    public string RemoteUserName { get => _remoteUserName; set => SetField(ref _remoteUserName, value); }
    public string RemotePassword { get => _remotePassword; set => SetField(ref _remotePassword, value); }
    public string Notes { get => _notes; set => SetField(ref _notes, value); }
    public string SupportedChannelsText { get => _supportedChannelsText; set => SetField(ref _supportedChannelsText, value); }
    public DateTimeOffset LastSeenAtUtc { get => _lastSeenAtUtc; set => SetFieldAndNotify(ref _lastSeenAtUtc, value, [nameof(PresenceSummary), nameof(LastSeenText)]); }
    public string PendingSupportRequestText { get => _pendingSupportRequestText; set => SetField(ref _pendingSupportRequestText, value); }
    public string ActiveSessionText { get => _activeSessionText; set => SetField(ref _activeSessionText, value); }
    public string ActiveSessionStatus { get => _activeSessionStatus; set => SetField(ref _activeSessionStatus, value); }
    public string AvailableUpdateVersion { get => _availableUpdateVersion; set => SetFieldAndNotify(ref _availableUpdateVersion, value, [nameof(UpdateSummary)]); }
    public bool IsUpdateAvailable { get => _isUpdateAvailable; set => SetFieldAndNotify(ref _isUpdateAvailable, value, [nameof(UpdateSummary)]); }
    public int UnreadClientChatCount { get => _unreadClientChatCount; set => SetFieldAndNotify(ref _unreadClientChatCount, value, [nameof(HasUnreadClientChat), nameof(ChatBadgeText), nameof(ChatSummary)]); }
    public DateTimeOffset? LastClientChatMessageAtUtc { get => _lastClientChatMessageAtUtc; set => SetFieldAndNotify(ref _lastClientChatMessageAtUtc, value, [nameof(ChatSummary)]); }
    public bool ChatViewedInSession { get => _chatViewedInSession; set => SetFieldAndNotify(ref _chatViewedInSession, value, [nameof(HasUnreadClientChat), nameof(ChatBadgeText), nameof(ChatSummary)]); }
    public RemoteChannel? ActiveChannel { get => _activeChannel; set => SetFieldAndNotify(ref _activeChannel, value, [nameof(HasActiveSession), nameof(HasLaunchableActiveSession), nameof(CanLaunchConnection)]); }
    public IReadOnlyList<ClientDiskUsageRow> DiskUsages { get => _diskUsages; set => SetFieldAndNotify(ref _diskUsages, value, [nameof(HasDiskUsage)]); }
    public long? TotalMemoryBytes { get => _totalMemoryBytes; set => SetFieldAndNotify(ref _totalMemoryBytes, value, [nameof(HasMemoryInfo), nameof(MemorySummary), nameof(MemoryBarWidth)]); }
    public long? AvailableMemoryBytes { get => _availableMemoryBytes; set => SetFieldAndNotify(ref _availableMemoryBytes, value, [nameof(HasMemoryInfo), nameof(MemorySummary), nameof(MemoryBarWidth)]); }
    public string OsDescription { get => _osDescription; set => SetFieldAndNotify(ref _osDescription, value, [nameof(SystemSummary)]); }
    public DateTimeOffset? LastBootAtUtc { get => _lastBootAtUtc; set => SetFieldAndNotify(ref _lastBootAtUtc, value, [nameof(UptimeSummary)]); }
    public IReadOnlyList<RemoteChannel> SupportedChannels { get; set; } = [];
    public IReadOnlyList<string> TailscaleIpAddresses { get; set; } = [];

    public bool HasActiveSession => ActiveChannel is not null;

    public bool HasLaunchableActiveSession => HasActiveSession && string.Equals(ActiveSessionStatus, "Active", StringComparison.OrdinalIgnoreCase);

    public bool IsDirectAdminAccessAvailable => IsOnline && IsAtLogonScreen;

    public bool CanLaunchConnection => IsOnline
        && (HasLaunchableActiveSession || IsDirectAdminAccessAvailable)
        && (ActiveChannel is RemoteChannel.Rdp or RemoteChannel.WinRm
            || (ActiveChannel is RemoteChannel.RustDesk && !string.IsNullOrWhiteSpace(RustDeskId)));

    public string StatusGlyph => IsOnline ? "🟢" : "🔴";
    public string ConnectionSummary => $"{MachineName} | {LogonStateSummary}";
    public string LogonStateSummary => IsAtLogonScreen
        ? "Login Screen"
        : string.IsNullOrWhiteSpace(CurrentUser)
            ? "No user"
            : CurrentUser;
    public string BatteryText => BatteryPercentage is >= 0 and <= 100 ? $"Battery {BatteryPercentage} %" : string.Empty;
    public string BatteryBadgeText => BatteryPercentage is >= 0 and <= 100 ? $"🔋 {BatteryPercentage}%" : string.Empty;
    public bool HasBatteryInfo => BatteryPercentage is >= 0 and <= 100;
    public bool HasDiskUsage => DiskUsages.Count > 0;
    public bool HasMemoryInfo => TotalMemoryBytes is > 0 && AvailableMemoryBytes is >= 0;
    public string CapabilitySummary => $"{SupportedChannelsText} | Agent {AgentVersion}";
    public string SystemSummary => string.IsNullOrWhiteSpace(OsDescription) ? string.Empty : OsDescription;
    public string UptimeSummary => LastBootAtUtc is null
        ? string.Empty
        : $"Uptime {FormatUptime(DateTimeOffset.UtcNow - LastBootAtUtc.Value)}";
    public string MemorySummary => !HasMemoryInfo
        ? string.Empty
        : $"RAM {FormatBytes((TotalMemoryBytes ?? 0) - (AvailableMemoryBytes ?? 0))} / {FormatBytes(TotalMemoryBytes ?? 0)}";
    public double MemoryBarWidth
    {
        get
        {
            var totalBytes = TotalMemoryBytes ?? 0;
            if (!HasMemoryInfo || totalBytes <= 0)
            {
                return 0;
            }

            var usedRatio = ((double)(totalBytes - (AvailableMemoryBytes ?? 0))) / totalBytes;
            return 164d * Math.Clamp(usedRatio, 0d, 1d);
        }
    }
    public string UpdateSummary => IsUpdateAvailable && !string.IsNullOrWhiteSpace(AvailableUpdateVersion)
        ? $"Update {AvailableUpdateVersion} available"
        : string.Empty;
    public bool HasUnreadClientChat => UnreadClientChatCount > 0 && !ChatViewedInSession;
    public string ChatBadgeText => HasUnreadClientChat ? $"Chat {UnreadClientChatCount}" : string.Empty;
    public string ChatSummary => LastClientChatMessageAtUtc is null
        ? string.Empty
        : HasUnreadClientChat
            ? $"New client messages: {UnreadClientChatCount}"
            : $"Last chat {LastClientChatMessageAtUtc:yyyy-MM-dd HH:mm:ss} UTC";
    public string PresenceStateText => IsOnline ? "Online" : "Offline";
    public string LastSeenText => $"Last seen {LastSeenAtUtc:yyyy-MM-dd HH:mm:ss} UTC";
    public string PresenceSummary => IsOnline ? $"Online | Last seen {LastSeenAtUtc:yyyy-MM-dd HH:mm:ss} UTC" : $"Offline | Last seen {LastSeenAtUtc:yyyy-MM-dd HH:mm:ss} UTC";

    public static ClientRow FromSummary(AdminClientSummary summary)
    {
        var row = new ClientRow();
        row.Apply(summary);
        return row;
    }

    public void Apply(AdminClientSummary summary)
    {
        ClientId = summary.ClientId;
        DeviceName = summary.DeviceName;
        MachineName = summary.MachineName;
        CurrentUser = summary.CurrentUser;
        HasInteractiveUser = summary.HasInteractiveUser;
        IsAtLogonScreen = summary.IsAtLogonScreen;
        AgentVersion = summary.AgentVersion;
        BatteryPercentage = summary.BatteryPercentage;
        DiskUsages = summary.DiskUsages
            .Select(static disk => new ClientDiskUsageRow
            {
                DriveName = disk.DriveName,
                TotalBytes = disk.TotalBytes,
                FreeBytes = disk.FreeBytes
            })
            .ToArray();
        TotalMemoryBytes = summary.TotalMemoryBytes;
        AvailableMemoryBytes = summary.AvailableMemoryBytes;
        OsDescription = summary.OsDescription ?? string.Empty;
        LastBootAtUtc = summary.LastBootAtUtc;
        LastSeenAtUtc = summary.LastSeenAtUtc;
        SetOnlineState(summary.IsOnline && DateTimeOffset.UtcNow - summary.LastSeenAtUtc <= OnlineThreshold);
        ConsentRequired = summary.ConsentRequired;
        AutoApproveSupportRequests = summary.AutoApproveSupportRequests;
        TailscaleConnected = summary.TailscaleConnected;
        TailscaleIpAddresses = summary.TailscaleIpAddresses.ToArray();
        TailscaleIpAddressesText = string.Join(", ", summary.TailscaleIpAddresses);
        RustDeskId = summary.RustDeskId ?? string.Empty;
        RustDeskPassword = summary.RustDeskPassword ?? string.Empty;
        RemoteUserName = summary.RemoteUserName ?? string.Empty;
        RemotePassword = summary.RemotePassword ?? string.Empty;
        Notes = summary.Notes ?? string.Empty;
        SupportedChannels = summary.SupportedChannels.ToArray();
        SupportedChannelsText = string.Join(", ", summary.SupportedChannels);
        ActiveChannel = summary.ActiveSession?.Channel;
        ActiveSessionStatus = summary.ActiveSession?.Status ?? string.Empty;
        UnreadClientChatCount = summary.UnreadClientChatCount;
        LastClientChatMessageAtUtc = summary.LastClientChatMessageAtUtc;
        ActiveSessionText = summary.ActiveSession is null
            ? string.Empty
            : $"{summary.ActiveSession.Channel} / {summary.ActiveSession.Status}";
        PendingSupportRequestText = summary.PendingSupportRequest is null
            ? string.Empty
            : $"{summary.PendingSupportRequest.PreferredChannel} / {summary.PendingSupportRequest.Status}";
        NotifyComputedProperties();
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(HasLaunchableActiveSession));
        OnPropertyChanged(nameof(IsDirectAdminAccessAvailable));
        OnPropertyChanged(nameof(CanLaunchConnection));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(BatteryBadgeText));
        OnPropertyChanged(nameof(HasBatteryInfo));
        OnPropertyChanged(nameof(HasDiskUsage));
        OnPropertyChanged(nameof(HasMemoryInfo));
        OnPropertyChanged(nameof(ConnectionSummary));
        OnPropertyChanged(nameof(LogonStateSummary));
        OnPropertyChanged(nameof(CapabilitySummary));
        OnPropertyChanged(nameof(SystemSummary));
        OnPropertyChanged(nameof(UptimeSummary));
        OnPropertyChanged(nameof(MemorySummary));
        OnPropertyChanged(nameof(MemoryBarWidth));
        OnPropertyChanged(nameof(UpdateSummary));
        OnPropertyChanged(nameof(HasUnreadClientChat));
        OnPropertyChanged(nameof(ChatBadgeText));
        OnPropertyChanged(nameof(ChatSummary));
        OnPropertyChanged(nameof(PresenceStateText));
        OnPropertyChanged(nameof(LastSeenText));
        OnPropertyChanged(nameof(PresenceSummary));
    }

    private void SetOnlineState(bool value)
    {
        if (_isOnline == value)
        {
            return;
        }

        _isOnline = value;
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(HasLaunchableActiveSession));
        OnPropertyChanged(nameof(IsDirectAdminAccessAvailable));
        OnPropertyChanged(nameof(CanLaunchConnection));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(PresenceStateText));
        OnPropertyChanged(nameof(LastSeenText));
        OnPropertyChanged(nameof(PresenceSummary));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void SetFieldAndNotify<T>(ref T field, T value, string[] additionalPropertyNames, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        foreach (var dependentPropertyName in additionalPropertyNames)
        {
            OnPropertyChanged(dependentPropertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        }

        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }

        return $"{Math.Max(0, (int)uptime.TotalMinutes)}m";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
