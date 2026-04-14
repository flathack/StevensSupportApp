using System.Windows;
using StevensSupportHelper.Admin.Models;
using StevensSupportHelper.Admin.Services;
using StevensSupportHelper.Shared.Diagnostics;

namespace StevensSupportHelper.Admin;

public partial class App : Application
{
    private readonly AdminApiClient _apiClient = new();
    private ResourceDictionary? _activeThemeDictionary;

    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            AppDiagnostics.WriteEvent("Admin", "DispatcherUnhandledException", "Unhandled dispatcher exception in admin UI.", eventArgs.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            AppDiagnostics.WriteEvent("Admin", "UnhandledException", "Unhandled exception reached AppDomain.CurrentDomain.", eventArgs.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            AppDiagnostics.WriteEvent("Admin", "UnobservedTaskException", "Unobserved task exception in admin UI.", eventArgs.Exception);
        };
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        AppDiagnostics.WriteEvent("Admin", "Startup", "Admin application starting.");
        _activeThemeDictionary = Resources.MergedDictionaries
            .FirstOrDefault(dictionary => dictionary.Source is not null &&
                                          dictionary.Source.OriginalString.Contains("Theme", StringComparison.OrdinalIgnoreCase) &&
                                          !dictionary.Source.OriginalString.Contains("BaseTheme", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(_apiClient);
        window.Closed += (_, _) => AppDiagnostics.WriteEvent("Admin", "Stopped", "Admin application window closed.");
        window.Show();
    }

    public static void ApplyTheme(AdminThemeMode themeMode)
    {
        if (Current is App app)
        {
            app.ApplyThemeInternal(themeMode);
        }
    }

    private void ApplyThemeInternal(AdminThemeMode themeMode)
    {
        var themeFile = themeMode == AdminThemeMode.Dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
        var replacement = new ResourceDictionary
        {
            Source = new Uri(themeFile, UriKind.Relative)
        };

        if (_activeThemeDictionary is not null)
        {
            Resources.MergedDictionaries.Remove(_activeThemeDictionary);
        }

        Resources.MergedDictionaries.Add(replacement);
        _activeThemeDictionary = replacement;
    }
}
