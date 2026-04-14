using StevensSupportHelper.Client.Service;
using StevensSupportHelper.Client.Service.Options;
using StevensSupportHelper.Client.Service.Services;
using StevensSupportHelper.Shared.Diagnostics;

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    AppDiagnostics.WriteEvent(
        "ClientService",
        "UnhandledException",
        "Unhandled exception reached AppDomain.CurrentDomain.",
        eventArgs.ExceptionObject as Exception);
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    AppDiagnostics.WriteEvent(
        "ClientService",
        "UnobservedTaskException",
        "Unobserved task exception was raised.",
        eventArgs.Exception);
};

var builder = Host.CreateApplicationBuilder(args);
var dynamicSettingsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "StevensSupportHelper",
    "dynamic-client-settings.json");
builder.Configuration.AddJsonFile(dynamicSettingsPath, optional: true, reloadOnChange: true);

builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection(ServiceOptions.SectionName));
builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "StevensSupportHelper Client Service";
});
builder.Services.AddSingleton<ClientIdentityStore>();
builder.Services.AddSingleton<ClientEnvironmentDiscoveryService>();
builder.Services.AddHttpClient<ServerApiClient>();
builder.Services.AddHttpClient<UpdateManifestClient>();
builder.Services.AddSingleton<ManagedFileTransferService>();
builder.Services.AddSingleton<AgentJobProcessor>();
builder.Services.AddSingleton<ClientUpdateCoordinator>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() => AppDiagnostics.WriteEvent("ClientService", "Started", "Client service host started."));
lifetime.ApplicationStopping.Register(() => AppDiagnostics.WriteEvent("ClientService", "Stopping", "Client service host stopping."));
lifetime.ApplicationStopped.Register(() => AppDiagnostics.WriteEvent("ClientService", "Stopped", "Client service host stopped."));

AppDiagnostics.WriteEvent("ClientService", "Startup", "Client service host initialized.");
host.Run();
