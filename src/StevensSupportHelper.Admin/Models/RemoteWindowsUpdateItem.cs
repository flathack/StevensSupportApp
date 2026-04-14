namespace StevensSupportHelper.Admin.Models;

public sealed record RemoteWindowsUpdateItem(
    string Title,
    string KbArticleIds,
    string Categories,
    bool IsDownloaded,
    long MaxDownloadSizeBytes);
