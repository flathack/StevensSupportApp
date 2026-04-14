namespace StevensSupportHelper.Admin.Models;

public sealed class AdminToastNotification
{
    public Guid NotificationId { get; init; } = Guid.NewGuid();
    public Guid ClientId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
