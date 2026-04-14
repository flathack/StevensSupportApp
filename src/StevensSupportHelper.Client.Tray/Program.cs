using StevensSupportHelper.Shared.Diagnostics;
using StevensSupportHelper.Client.Tray;

Application.ThreadException += (_, eventArgs) =>
{
    AppDiagnostics.WriteEvent("ClientTray", "ThreadException", "Unhandled Windows Forms thread exception.", eventArgs.Exception);
};

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    AppDiagnostics.WriteEvent("ClientTray", "UnhandledException", "Unhandled exception reached AppDomain.CurrentDomain.", eventArgs.ExceptionObject as Exception);
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    AppDiagnostics.WriteEvent("ClientTray", "UnobservedTaskException", "Unobserved task exception in tray application.", eventArgs.Exception);
};

Application.ApplicationExit += (_, _) =>
{
    AppDiagnostics.WriteEvent("ClientTray", "Stopped", "Tray application exited.");
};

AppDiagnostics.WriteEvent("ClientTray", "Startup", "Tray application starting.");
ApplicationConfiguration.Initialize();
Application.Run(new TrayApplicationContext());
