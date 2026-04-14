using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;

namespace StevensSupportHelper.Admin;

public partial class ScreenshotPreviewTab : UserControl
{
    private readonly ClientRow _client;
    private readonly PowerShellRemoteAdminService _remoteService;
    private bool _isBusy;

    public ScreenshotPreviewTab(ClientRow client, PowerShellRemoteAdminService remoteService)
    {
        _client = client;
        _remoteService = remoteService;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            ToggleBusy(true, "Capturing screenshot...");
            var bytes = await _remoteService.CaptureScreenshotAsync(_client, CancellationToken.None);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            PreviewImage.Source = image;
            ToggleBusy(false, $"Loaded screenshot from {_client.DeviceName}.");
        }
        catch (Exception exception)
        {
            ToggleBusy(false, $"Screenshot failed: {exception.Message}");
        }
    }

    private void ToggleBusy(bool isBusy, string status)
    {
        _isBusy = isBusy;
        RefreshButton.IsEnabled = !isBusy;
        StatusTextBlock.Text = status;
    }
}
