using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

string? workingDirectory = null;
bool silentMode = false;
bool updateOnlyRun = false;

try
{
    Console.Title = "StevensSupportHelper Client Installer";
    InstallerRuntimeContext.InstallerLogPath = InitializeInstallerLog();
    LogStep("Installer start");
    var arguments = InstallerArguments.Parse(args);
    silentMode = arguments.Silent;
    updateOnlyRun = arguments.UpdateOnly;
    var installerState = LoadInstallerState();
    if (!IsAdministrator())
    {
        throw new InvalidOperationException("ClientSetup.exe must be run in an elevated Administrator session.");
    }

    if (!arguments.Silent)
    {
        ApplicationConfiguration.Initialize();
        var configuredArguments = ShowInstallerForm(arguments);
        if (configuredArguments is null)
        {
            return;
        }

        arguments = configuredArguments;
    }

    ValidateArguments(arguments);
    LogStep($"Using server URL {arguments.ServerUrl}");
    LogStep($"Using install root {arguments.InstallRoot}");

    if (!arguments.UpdateOnly && arguments.InstallRustDesk)
    {
        if (IsRustDeskInstalled())
        {
            LogStep("RustDesk is already installed. Skipping installation.");
        }
        else if (!string.IsNullOrWhiteSpace(arguments.RustDeskInstallerFileName))
        {
            LogStep($"Installing RustDesk from local package {arguments.RustDeskInstallerFileName}");
            InstallRustDeskFromLocalPackage(arguments.RustDeskInstallerFileName);
        }
        else
        {
            LogStep("Installing RustDesk");
            InstallRustDeskWithWinget();
        }
    }

    if (!arguments.UpdateOnly && arguments.InstallTailscale)
    {
        if (IsTailscaleInstalled())
        {
            LogStep("Tailscale is already installed. Skipping installation.");
        }
        else if (!string.IsNullOrWhiteSpace(arguments.TailscaleInstallerFileName))
        {
            LogStep($"Installing Tailscale from local package {arguments.TailscaleInstallerFileName}");
            InstallTailscaleFromLocalPackage(arguments.TailscaleInstallerFileName);
        }
        else
        {
            LogStep("Installing Tailscale");
            InstallTailscaleWithWinget();
        }

        var tailscaleAuthKey = arguments.TailscaleAuthKey;
        if (string.IsNullOrWhiteSpace(tailscaleAuthKey))
        {
            tailscaleAuthKey = ReadSidecarValue("tailscale.txt");
        }
        if (!string.IsNullOrWhiteSpace(tailscaleAuthKey))
        {
            if (ShouldSkipTailscaleAuth(installerState, tailscaleAuthKey))
            {
                LogStep("Tailscale auth key configuration is already applied. Skipping.");
            }
            else
            {
                LogStep("Applying Tailscale auth key");
                ConfigureTailscaleAuthKey(tailscaleAuthKey);
            }
        }
    }

    if (!arguments.UpdateOnly && !string.IsNullOrWhiteSpace(arguments.RustDeskPassword))
    {
        if (ShouldSkipRustDeskPassword(installerState, arguments.RustDeskPassword))
        {
            LogStep("RustDesk permanent password step is already applied. Skipping.");
        }
        else
        {
            LogStep("Configuring RustDesk permanent password");
            ConfigureRustDeskPermanentPassword(arguments.RustDeskPassword);
        }
    }

    if (!arguments.UpdateOnly && ShouldSkipRustDeskDirectAccess(installerState))
    {
        LogStep("RustDesk direct IP access is already configured. Skipping.");
    }
    else if (!arguments.UpdateOnly)
    {
        LogStep("Configuring RustDesk direct IP access");
        ConfigureRustDeskDirectAccess();
    }

    if (!arguments.UpdateOnly && arguments.EnableRdp)
    {
        if (ShouldSkipRemoteDesktop(installerState, arguments))
        {
            LogStep("RDP step already handled for this machine/configuration. Skipping.");
        }
        else
        {
            LogStep("Configuring RDP");
            EnableRemoteDesktop(arguments);
        }
    }

    if (!arguments.UpdateOnly && IsWinRmHttpsConfigured())
    {
        LogStep("WinRM over HTTPS already configured. Skipping.");
    }
    else if (!arguments.UpdateOnly)
    {
        LogStep("Configuring WinRM over HTTPS");
        EnableWinRmRemoting();
    }

    using var payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClientPayload.zip")
        ?? throw new InvalidOperationException("Embedded client payload was not found. Rebuild the installer with the payload bundle.");

    workingDirectory = Path.Combine(Path.GetTempPath(), "StevensSupportHelperInstaller", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workingDirectory);
    LogStep($"Extracting payload to {workingDirectory}");
    var payloadZipPath = Path.Combine(workingDirectory, "client-payload.zip");
    await using (var output = File.Create(payloadZipPath))
    {
        await payloadStream.CopyToAsync(output);
    }

    var extractDirectory = Path.Combine(workingDirectory, "payload");
    System.IO.Compression.ZipFile.ExtractToDirectory(payloadZipPath, extractDirectory);

    LogStep("Preparing existing installation");
    PrepareExistingInstall(arguments);

    var serviceSource = Path.Combine(extractDirectory, "client-service");
    var traySource = Path.Combine(extractDirectory, "client-tray");
    var serviceDestination = Path.Combine(arguments.InstallRoot, "client-service");
    var trayDestination = Path.Combine(arguments.InstallRoot, "client-tray");
    var hadExistingService = ServiceExists(arguments.ServiceName);
    var updateBackup = arguments.UpdateOnly
        ? CreateUpdateBackup(arguments.InstallRoot, serviceDestination, trayDestination)
        : null;

    try
    {
        LogStep("Copying service payload");
        CopyDirectory(
            serviceSource,
            serviceDestination,
            arguments.UpdateOnly
                ? [ "appsettings.json" ]
                : []);
        LogStep("Copying tray payload");
        CopyDirectoryWithTrayRetry(traySource, trayDestination);

        if (arguments.UpdateOnly)
        {
            LogStep("Update-only mode active. Preserving existing client configuration.");
        }
        else
        {
            LogStep("Writing client configuration");
            UpdateServiceSettings(
                Path.Combine(serviceDestination, "appsettings.json"),
                arguments.ServerUrl,
                arguments.DeviceName,
                arguments.RegistrationSharedKey,
                arguments.EnableAutoApprove,
                arguments.RustDeskId,
                arguments.TailscaleIpAddresses);
            WriteTraySettings(arguments.ServerUrl, trayDestination);

            LogStep("Preparing optional admin user");
            EnsureAdminUser(arguments);
        }
        LogStep("Installing Windows service");
        InstallOrReplaceService(arguments.ServiceName, Path.Combine(serviceDestination, "StevensSupportHelper.Client.Service.exe"), ServiceCredentials.LocalSystem);
        LogStep("Installing tray startup");
        InstallTrayStartup(Path.Combine(trayDestination, "StevensSupportHelper.Client.Tray.exe"));
        LogStep("Starting tray application");
        StartTrayProcess(Path.Combine(trayDestination, "StevensSupportHelper.Client.Tray.exe"));
        CleanupUpdateBackup(updateBackup);
    }
    catch
    {
        if (arguments.UpdateOnly && updateBackup is not null)
        {
            LogStep("Update-only installation failed. Attempting rollback of previous client files.");
            TryRollbackUpdate(arguments, updateBackup);
        }
        else if (hadExistingService)
        {
            TryRestartExistingService(arguments);
        }

        throw;
    }

    if (!arguments.UpdateOnly)
    {
        SaveInstallerState(CreateInstallerState(arguments, installerState));
    }

    LogStep("Client components installed successfully");
    Console.WriteLine($"Install root: {arguments.InstallRoot}");
    Console.WriteLine($"Service name: {arguments.ServiceName}");
    Console.WriteLine($"Server URL: {arguments.ServerUrl}");
    Console.WriteLine($"Device name: {arguments.DeviceName}");
}
catch (Exception exception)
{
    LogStep($"Installer failed: {exception}");
    Console.Error.WriteLine(exception.Message);
    if (!silentMode)
    {
        MessageBox.Show(
            exception.Message,
            "StevensSupportHelper Installer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
    Environment.ExitCode = 1;
}
finally
{
    TryDeleteDirectory(workingDirectory);
    if (updateOnlyRun)
    {
        TryScheduleSelfCleanup();
    }
}

static bool IsAdministrator()
{
    var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}

static void ValidateArguments(InstallerArguments arguments)
{
    if (arguments.UpdateOnly)
    {
        return;
    }

    if (!Uri.TryCreate(arguments.ServerUrl, UriKind.Absolute, out var serverUri) ||
        (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException("Server URL must be an absolute http:// or https:// URL.");
    }

    if (string.IsNullOrWhiteSpace(arguments.DeviceName))
    {
        throw new InvalidOperationException("Client name is required.");
    }

    if (string.IsNullOrWhiteSpace(arguments.RegistrationSharedKey))
    {
        throw new InvalidOperationException("API key / registration key is required.");
    }
}

static void LogStep(string message)
{
    var formatted = $"[{DateTime.Now:HH:mm:ss}] {message}";
    Console.WriteLine(formatted);
    TryAppendInstallerLog(formatted);
}

static string InitializeInstallerLog()
{
    var logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "InstallerLogs");
    Directory.CreateDirectory(logDirectory);
    var logPath = Path.Combine(logDirectory, $"installer-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    File.WriteAllText(logPath, $"StevensSupportHelper Installer Log{Environment.NewLine}", Encoding.UTF8);
    return logPath;
}

static void TryAppendInstallerLog(string message)
{
    try
    {
        if (string.IsNullOrWhiteSpace(InstallerRuntimeContext.InstallerLogPath))
        {
            return;
        }

        File.AppendAllText(InstallerRuntimeContext.InstallerLogPath, message + Environment.NewLine, Encoding.UTF8);
    }
    catch
    {
    }
}

static void TryDeleteDirectory(string? path)
{
    try
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
    catch
    {
    }
}

static UpdateBackupContext? CreateUpdateBackup(string installRoot, string serviceDestination, string trayDestination)
{
    var backupRoot = Path.Combine(
        installRoot,
        ".update-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
    var serviceBackup = Path.Combine(backupRoot, "client-service");
    var trayBackup = Path.Combine(backupRoot, "client-tray");

    Directory.CreateDirectory(backupRoot);

    if (Directory.Exists(serviceDestination))
    {
        LogStep($"Creating service backup at {serviceBackup}");
        CopyDirectory(serviceDestination, serviceBackup);
    }

    if (Directory.Exists(trayDestination))
    {
        LogStep($"Creating tray backup at {trayBackup}");
        CopyDirectory(trayDestination, trayBackup);
    }

    return new UpdateBackupContext(backupRoot, serviceBackup, trayBackup);
}

static void TryRollbackUpdate(InstallerArguments arguments, UpdateBackupContext backup)
{
    try
    {
        var serviceDestination = Path.Combine(arguments.InstallRoot, "client-service");
        var trayDestination = Path.Combine(arguments.InstallRoot, "client-tray");

        if (Directory.Exists(serviceDestination))
        {
            Directory.Delete(serviceDestination, recursive: true);
        }

        if (Directory.Exists(trayDestination))
        {
            Directory.Delete(trayDestination, recursive: true);
        }

        if (Directory.Exists(backup.ServiceBackupPath))
        {
            LogStep("Restoring previous service files from backup.");
            CopyDirectory(backup.ServiceBackupPath, serviceDestination);
        }

        if (Directory.Exists(backup.TrayBackupPath))
        {
            LogStep("Restoring previous tray files from backup.");
            CopyDirectory(backup.TrayBackupPath, trayDestination);
        }

        var serviceExecutable = Path.Combine(serviceDestination, "StevensSupportHelper.Client.Service.exe");
        if (File.Exists(serviceExecutable))
        {
            LogStep("Restarting previous Windows service after rollback.");
            InstallOrReplaceService(arguments.ServiceName, serviceExecutable, ServiceCredentials.LocalSystem);
        }

        var trayExecutable = Path.Combine(trayDestination, "StevensSupportHelper.Client.Tray.exe");
        if (File.Exists(trayExecutable))
        {
            LogStep("Restarting previous tray after rollback.");
            StartTrayProcess(trayExecutable);
        }
    }
    catch (Exception rollbackException)
    {
        LogStep($"Rollback failed: {rollbackException}");
    }
}

static void CleanupUpdateBackup(UpdateBackupContext? backup)
{
    if (backup is null)
    {
        return;
    }

    LogStep($"Cleaning update backup {backup.BackupRoot}");
    TryDeleteDirectory(backup.BackupRoot);
}

static void TryScheduleSelfCleanup()
{
    try
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory) ||
            directory.IndexOf(@"\ProgramData\StevensSupportHelper\AdminUpdates\", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return;
        }

        var command = $"/c ping 127.0.0.1 -n 4 > nul && del /f /q \"{executablePath}\" && rmdir /s /q \"{directory}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = command,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        LogStep($"Scheduled cleanup for {directory}");
    }
    catch (Exception exception)
    {
        LogStep($"Cleanup scheduling failed: {exception.Message}");
    }
}

static InstallerArguments? ShowInstallerForm(InstallerArguments initialArguments)
{
    using var form = new Form
    {
        Text = "StevensSupportHelper Client Setup",
        Width = 700,
        Height = 820,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false,
        StartPosition = FormStartPosition.CenterScreen
    };

    var table = new TableLayoutPanel
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(18),
        ColumnCount = 2,
        RowCount = 17
    };
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    for (var i = 0; i < 16; i++)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
    }
    table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    var deviceNameLabel = new Label { Text = "Client Name", AutoSize = true, Anchor = AnchorStyles.Left };
    var deviceNameTextBox = new TextBox { Text = initialArguments.DeviceName, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var serverUrlLabel = new Label { Text = "Server URL", AutoSize = true, Anchor = AnchorStyles.Left };
    var serverUrlTextBox = new TextBox { Text = initialArguments.ServerUrl, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var apiKeyLabel = new Label { Text = "API Key", AutoSize = true, Anchor = AnchorStyles.Left };
    var apiKeyTextBox = new TextBox { Text = initialArguments.RegistrationSharedKey, Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
    var installRootLabel = new Label { Text = "Install Root", AutoSize = true, Anchor = AnchorStyles.Left };
    var installRootTextBox = new TextBox { Text = initialArguments.InstallRoot, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var installRustDeskLabel = new Label { Text = "RustDesk", AutoSize = true, Anchor = AnchorStyles.Left };
    var installRustDeskCheckBox = new CheckBox { Checked = initialArguments.InstallRustDesk, AutoSize = true, Text = "Per winget installieren", Anchor = AnchorStyles.Left };
    var installTailscaleLabel = new Label { Text = "Tailscale", AutoSize = true, Anchor = AnchorStyles.Left };
    var installTailscaleCheckBox = new CheckBox { Checked = initialArguments.InstallTailscale, AutoSize = true, Text = "Per winget installieren", Anchor = AnchorStyles.Left };
    var tailscaleAuthKeyLabel = new Label { Text = "Tailscale Key", AutoSize = true, Anchor = AnchorStyles.Left };
    var tailscaleAuthKeyTextBox = new TextBox { Text = initialArguments.TailscaleAuthKey, Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
    var rustDeskIdLabel = new Label { Text = "RustDesk ID", AutoSize = true, Anchor = AnchorStyles.Left };
    var rustDeskIdTextBox = new TextBox { Text = DetectRustDeskId(initialArguments.RustDeskId) ?? string.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var rustDeskPasswordLabel = new Label { Text = "RustDesk PW", AutoSize = true, Anchor = AnchorStyles.Left };
    var rustDeskPasswordTextBox = new TextBox { Text = initialArguments.RustDeskPassword, Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
    var tailscaleIpsLabel = new Label { Text = "Tailscale IP(s)", AutoSize = true, Anchor = AnchorStyles.Left };
    var detectedTailscaleIps = initialArguments.TailscaleIpAddresses.Count > 0 ? initialArguments.TailscaleIpAddresses : DetectTailscaleIpAddresses();
    var tailscaleIpsTextBox = new TextBox { Text = string.Join(", ", detectedTailscaleIps), Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var autoApproveLabel = new Label { Text = "Auto Approve", AutoSize = true, Anchor = AnchorStyles.Left };
    var autoApproveCheckBox = new CheckBox { Checked = initialArguments.EnableAutoApprove, AutoSize = true, Text = "Support-Anfragen automatisch genehmigen", Anchor = AnchorStyles.Left };
    var enableRdpLabel = new Label { Text = "RDP", AutoSize = true, Anchor = AnchorStyles.Left };
    var enableRdpCheckBox = new CheckBox { Checked = initialArguments.EnableRdp, AutoSize = true, Text = "RDP aktivieren / vorbereiten", Anchor = AnchorStyles.Left };
    var createServiceUserLabel = new Label { Text = "Admin User", AutoSize = true, Anchor = AnchorStyles.Left };
    var createServiceUserCheckBox = new CheckBox { Checked = initialArguments.CreateServiceUser, AutoSize = true, Text = "Lokalen Admin-/Hilfsuser anlegen", Anchor = AnchorStyles.Left };
    var serviceUserAdminLabel = new Label { Text = "User Admin", AutoSize = true, Anchor = AnchorStyles.Left };
    var serviceUserAdminCheckBox = new CheckBox { Checked = initialArguments.ServiceUserIsAdministrator, AutoSize = true, Text = "Zusatzuser zu Administrators hinzufuegen", Anchor = AnchorStyles.Left };
    var serviceUserNameLabel = new Label { Text = "Username", AutoSize = true, Anchor = AnchorStyles.Left };
    var serviceUserNameTextBox = new TextBox { Text = initialArguments.ServiceUserName, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    var serviceUserPasswordLabel = new Label { Text = "Svc Password", AutoSize = true, Anchor = AnchorStyles.Left };
    var serviceUserPasswordTextBox = new TextBox { Text = initialArguments.ServiceUserPassword, Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };

    table.Controls.Add(deviceNameLabel, 0, 0);
    table.Controls.Add(deviceNameTextBox, 1, 0);
    table.Controls.Add(serverUrlLabel, 0, 1);
    table.Controls.Add(serverUrlTextBox, 1, 1);
    table.Controls.Add(apiKeyLabel, 0, 2);
    table.Controls.Add(apiKeyTextBox, 1, 2);
    table.Controls.Add(installRootLabel, 0, 3);
    table.Controls.Add(installRootTextBox, 1, 3);
    table.Controls.Add(installRustDeskLabel, 0, 4);
    table.Controls.Add(installRustDeskCheckBox, 1, 4);
    table.Controls.Add(installTailscaleLabel, 0, 5);
    table.Controls.Add(installTailscaleCheckBox, 1, 5);
    table.Controls.Add(tailscaleAuthKeyLabel, 0, 6);
    table.Controls.Add(tailscaleAuthKeyTextBox, 1, 6);
    table.Controls.Add(rustDeskIdLabel, 0, 7);
    table.Controls.Add(rustDeskIdTextBox, 1, 7);
    table.Controls.Add(rustDeskPasswordLabel, 0, 8);
    table.Controls.Add(rustDeskPasswordTextBox, 1, 8);
    table.Controls.Add(tailscaleIpsLabel, 0, 9);
    table.Controls.Add(tailscaleIpsTextBox, 1, 9);
    table.Controls.Add(autoApproveLabel, 0, 10);
    table.Controls.Add(autoApproveCheckBox, 1, 10);
    table.Controls.Add(enableRdpLabel, 0, 11);
    table.Controls.Add(enableRdpCheckBox, 1, 11);
    table.Controls.Add(createServiceUserLabel, 0, 12);
    table.Controls.Add(createServiceUserCheckBox, 1, 12);
    table.Controls.Add(serviceUserAdminLabel, 0, 13);
    table.Controls.Add(serviceUserAdminCheckBox, 1, 13);
    table.Controls.Add(serviceUserNameLabel, 0, 14);
    table.Controls.Add(serviceUserNameTextBox, 1, 14);
    table.Controls.Add(serviceUserPasswordLabel, 0, 15);
    table.Controls.Add(serviceUserPasswordTextBox, 1, 15);

    var infoLabel = new Label
    {
        Text = "Der Installer entfernt eine vorhandene Installation zuerst vollstaendig und installiert den Client danach frisch neu. Optional werden RustDesk und Tailscale vorher installiert. Auto Approve ist fuer unbeaufsichtigte Systeme gedacht.",
        AutoSize = true,
        MaximumSize = new Size(480, 0),
        ForeColor = System.Drawing.Color.FromArgb(70, 80, 90),
        Margin = new Padding(0, 12, 0, 12)
    };
    table.Controls.Add(infoLabel, 0, 16);
    table.SetColumnSpan(infoLabel, 2);

    var buttonsPanel = new FlowLayoutPanel
    {
        Dock = DockStyle.Bottom,
        FlowDirection = FlowDirection.RightToLeft,
        Padding = new Padding(18, 0, 18, 18),
        Height = 54
    };

    var installButton = new Button
    {
        Text = "Install",
        DialogResult = DialogResult.OK,
        Width = 110
    };
    var cancelButton = new Button
    {
        Text = "Cancel",
        DialogResult = DialogResult.Cancel,
        Width = 110
    };
    buttonsPanel.Controls.Add(installButton);
    buttonsPanel.Controls.Add(cancelButton);

    form.AcceptButton = installButton;
    form.CancelButton = cancelButton;
    form.Controls.Add(table);
    form.Controls.Add(buttonsPanel);

    if (form.ShowDialog() != DialogResult.OK)
    {
        return null;
    }

    return initialArguments with
    {
        DeviceName = deviceNameTextBox.Text.Trim(),
        ServerUrl = serverUrlTextBox.Text.Trim(),
        RegistrationSharedKey = apiKeyTextBox.Text.Trim(),
        InstallRoot = installRootTextBox.Text.Trim(),
        InstallRustDesk = installRustDeskCheckBox.Checked,
        InstallTailscale = installTailscaleCheckBox.Checked,
        TailscaleAuthKey = tailscaleAuthKeyTextBox.Text.Trim(),
        EnableAutoApprove = autoApproveCheckBox.Checked,
        EnableRdp = enableRdpCheckBox.Checked,
        CreateServiceUser = createServiceUserCheckBox.Checked,
        ServiceUserIsAdministrator = serviceUserAdminCheckBox.Checked,
        ServiceUserName = serviceUserNameTextBox.Text.Trim(),
        ServiceUserPassword = serviceUserPasswordTextBox.Text,
        RustDeskId = rustDeskIdTextBox.Text.Trim(),
        RustDeskPassword = rustDeskPasswordTextBox.Text,
        TailscaleIpAddresses = ParseIpList(tailscaleIpsTextBox.Text)
    };
}

static void InstallRustDeskWithWinget()
{
    var wingetPath = ResolveWingetPath();
    if (wingetPath is null)
    {
        throw new InvalidOperationException("RustDesk installation was requested, but winget.exe is not available on this machine.");
    }

    InstallWithWinget(
        wingetPath,
        "RustDesk",
        "install --id RustDesk.RustDesk -e --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements");

    DismissCompatibilityAssistant();
    WaitForExecutable("RustDesk", ResolveRustDeskExecutable, TimeSpan.FromMinutes(2));
}

static void InstallRustDeskFromLocalPackage(string fileName)
{
    var packagePath = ResolveInstallerSidecarPath(fileName);
    var extension = Path.GetExtension(packagePath);
    if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
    {
        RunProcess(
            "msiexec.exe",
            $"/i \"{packagePath}\" /qn /norestart INSTALLPRINTER=\"N\" CREATEDESKTOPSHORTCUTS=\"N\"",
            throwOnFailure: true,
            timeoutMilliseconds: 600000);
    }
    else if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
    {
        RunProcess(
            packagePath,
            "--silent-install",
            throwOnFailure: true,
            timeoutMilliseconds: 600000);
    }
    else
    {
        throw new InvalidOperationException($"RustDesk local installer '{fileName}' must be an .msi or .exe file.");
    }

    DismissCompatibilityAssistant();
    WaitForExecutable("RustDesk", ResolveRustDeskExecutable, TimeSpan.FromMinutes(3));
}

static void InstallTailscaleWithWinget()
{
    var wingetPath = ResolveWingetPath();
    if (wingetPath is null)
    {
        throw new InvalidOperationException("Tailscale installation was requested, but winget.exe is not available on this machine.");
    }

    InstallWithWinget(
        wingetPath,
        "Tailscale",
        "install --id Tailscale.Tailscale -e --source winget --silent --disable-interactivity --accept-package-agreements --accept-source-agreements --override \"/quiet TS_UNATTENDEDMODE=always TS_ALLOWINCOMINGCONNECTIONS=always\"");

    WaitForExecutable("Tailscale", ResolveTailscaleExecutable, TimeSpan.FromMinutes(2));
}

static void InstallTailscaleFromLocalPackage(string fileName)
{
    var packagePath = ResolveInstallerSidecarPath(fileName);
    var extension = Path.GetExtension(packagePath);
    if (extension.Equals(".msi", StringComparison.OrdinalIgnoreCase))
    {
        RunProcess(
            "msiexec.exe",
            $"/i \"{packagePath}\" /qn /norestart TS_UNATTENDEDMODE=\"always\" TS_ALLOWINCOMINGCONNECTIONS=\"always\" TS_NOLAUNCH=\"1\"",
            throwOnFailure: true,
            timeoutMilliseconds: 600000);
    }
    else if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
    {
        RunProcess(
            packagePath,
            "/install /quiet /norestart",
            throwOnFailure: true,
            timeoutMilliseconds: 600000);
    }
    else
    {
        throw new InvalidOperationException($"Tailscale local installer '{fileName}' must be an .msi or .exe file.");
    }

    WaitForExecutable("Tailscale", ResolveTailscaleExecutable, TimeSpan.FromMinutes(3));
}

static void InstallWithWinget(string wingetPath, string packageName, string installArguments)
{
    var result = RunProcess(wingetPath, installArguments, throwOnFailure: false);
    if (result.ExitCode == 0)
    {
        return;
    }

    var combinedOutput = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
    if (LooksLikeWingetSourceFailure(combinedOutput))
    {
        LogStep("winget source issue detected. Attempting repair.");
        RunProcess(wingetPath, "source reset --force", throwOnFailure: false, timeoutMilliseconds: 120000);
        RunProcess(wingetPath, "source update", throwOnFailure: false, timeoutMilliseconds: 120000);
        result = RunProcess(wingetPath, installArguments, throwOnFailure: false);
        if (result.ExitCode == 0)
        {
            return;
        }
    }

    throw new InvalidOperationException($"winget {packageName} installation failed: {SummarizeProcessOutput(result.StandardOutput, result.StandardError)}");
}

static bool LooksLikeWingetSourceFailure(string output)
{
    return output.IndexOf("Failed in attempting to update the source", StringComparison.OrdinalIgnoreCase) >= 0 ||
           output.IndexOf("InternetOpenUrl() failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
           output.IndexOf("0x80072ee7", StringComparison.OrdinalIgnoreCase) >= 0;
}

static string ResolveInstallerSidecarPath(string fileName)
{
    var packagePath = Path.Combine(AppContext.BaseDirectory, fileName);
    if (!File.Exists(packagePath))
    {
        throw new InvalidOperationException($"Configured installer package '{fileName}' was not found next to the installer EXE.");
    }

    return packagePath;
}

static void ConfigureTailscaleAuthKey(string authKey)
{
    var tailscalePath = ResolveTailscaleExecutable();
    if (tailscalePath is null)
    {
        throw new InvalidOperationException("A Tailscale auth key was provided, but tailscale.exe could not be found after installation.");
    }

    var escapedAuthKey = authKey.Replace("\"", "\\\"", StringComparison.Ordinal);
    WaitForExecutable("Tailscale", ResolveTailscaleExecutable, TimeSpan.FromMinutes(2));
    WaitForServiceRunning("Tailscale", TimeSpan.FromMinutes(1));

    var result = RunProcess(
        tailscalePath,
        $"up --auth-key \"{escapedAuthKey}\" --accept-routes --accept-dns=false --reset",
        throwOnFailure: false);

    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException($"Tailscale login with auth key failed: {result.StandardOutput}{Environment.NewLine}{result.StandardError}".Trim());
    }
}

static void ConfigureRustDeskPermanentPassword(string password)
{
    var rustDeskPath = ResolveRustDeskExecutable();
    if (rustDeskPath is null)
    {
        throw new InvalidOperationException("A RustDesk password was provided, but RustDesk.exe could not be found after installation.");
    }

    WaitForExecutable("RustDesk", ResolveRustDeskExecutable, TimeSpan.FromMinutes(2));

    var escapedPassword = password.Replace("\"", "\\\"", StringComparison.Ordinal);
    var result = RunProcess(
        rustDeskPath,
        $"--password {escapedPassword}",
        throwOnFailure: false);

    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException($"RustDesk password configuration failed: {result.StandardOutput}{Environment.NewLine}{result.StandardError}".Trim());
    }
}

static void ConfigureRustDeskDirectAccess()
{
    var rustDeskPath = ResolveRustDeskExecutable();
    if (rustDeskPath is null)
    {
        return;
    }

    WaitForExecutable("RustDesk", ResolveRustDeskExecutable, TimeSpan.FromMinutes(2));

    var tempConfigPath = Path.Combine(Path.GetTempPath(), $"rustdesk-import-{Guid.NewGuid():N}.toml");
    File.WriteAllText(
        tempConfigPath,
        """
        direct-server = "Y"
        direct-access-port = 21118
        enable-tunnel = "Y"
        allow-remote-config-modification = "Y"
        """);

    try
    {
        var result = RunProcess(
            rustDeskPath,
            $"--import-config \"{tempConfigPath}\"",
            throwOnFailure: false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"RustDesk direct-access configuration failed: {result.StandardOutput}{Environment.NewLine}{result.StandardError}".Trim());
        }
    }
    finally
    {
        if (File.Exists(tempConfigPath))
        {
            File.Delete(tempConfigPath);
        }
    }
}

static bool IsRustDeskInstalled()
{
    if (!string.IsNullOrWhiteSpace(ResolveRustDeskExecutable()))
    {
        return true;
    }

    return IsProgramRegistered("RustDesk");
}

static bool IsTailscaleInstalled()
{
    if (!string.IsNullOrWhiteSpace(ResolveTailscaleExecutable()))
    {
        return true;
    }

    if (ServiceExists("Tailscale"))
    {
        return true;
    }

    return IsProgramRegistered("Tailscale");
}

static bool IsProgramRegistered(string displayNameFragment)
{
    string[] uninstallRoots =
    [
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    foreach (var root in uninstallRoots)
    {
        var result = RunProcess("reg.exe", $"query \"{root}\" /s /f \"{displayNameFragment}\"", throwOnFailure: false, timeoutMilliseconds: 60000);
        var output = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
        if (output.IndexOf("DisplayName", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
    }

    return false;
}

static void WaitForExecutable(string productName, Func<string?> resolver, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < deadline)
    {
        var path = resolver();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            LogStep($"{productName} detected at {path}");
            return;
        }

        Thread.Sleep(2000);
    }

    throw new InvalidOperationException($"{productName} installation did not finish in time.");
}

static void WaitForServiceRunning(string serviceName, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow.Add(timeout);
    while (DateTime.UtcNow < deadline)
    {
        var queryResult = RunProcess("sc.exe", $"query \"{serviceName}\"", throwOnFailure: false);
        var output = $"{queryResult.StandardOutput}{Environment.NewLine}{queryResult.StandardError}";
        if (output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            LogStep($"{serviceName} service is running");
            return;
        }

        if (output.IndexOf("STOPPED", StringComparison.OrdinalIgnoreCase) >= 0 ||
            output.IndexOf("STOP_PENDING", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try
            {
                RunProcess("sc.exe", $"start \"{serviceName}\"", throwOnFailure: false);
            }
            catch
            {
            }
        }

        Thread.Sleep(2000);
    }

    throw new InvalidOperationException($"{serviceName} service did not reach Running state in time.");
}

static void DismissCompatibilityAssistant()
{
    try
    {
        foreach (var processName in new[] { "pcaui", "pcalua" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }
        }
    }
    catch
    {
    }
}

static void EnableRemoteDesktop(InstallerArguments arguments)
{
    var edition = GetWindowsEdition();
    LogStep($"Detected Windows edition: {edition}");
    if (edition.Contains("Home", StringComparison.OrdinalIgnoreCase))
    {
        LogStep("Windows Home detected. Skipping RDP activation because no built-in RDP host is available.");
        return;
    }

    using (var terminalServerKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server", writable: true))
    {
        if (terminalServerKey is null)
        {
            throw new InvalidOperationException("Terminal Server registry key was not found.");
        }

        terminalServerKey.SetValue("fDenyTSConnections", 0, RegistryValueKind.DWord);
        terminalServerKey.SetValue("fAllowToGetHelp", 1, RegistryValueKind.DWord);
    }

    RunProcess("netsh.exe", "advfirewall firewall set rule group=\"remote desktop\" new enable=Yes", throwOnFailure: false, timeoutMilliseconds: 60000);
    RunProcess("sc.exe", "config TermService start= demand", throwOnFailure: false, timeoutMilliseconds: 60000);
    RunProcess("sc.exe", "start TermService", throwOnFailure: false, timeoutMilliseconds: 60000);
}

static string GetWindowsEdition()
{
    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
    return key?.GetValue("ProductName")?.ToString() ?? "Unknown";
}

static InstallerState? LoadInstallerState()
{
    var path = GetInstallerStatePath();
    if (!File.Exists(path))
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<InstallerState>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    catch
    {
        return null;
    }
}

static void SaveInstallerState(InstallerState state)
{
    var path = GetInstallerStatePath();
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

static InstallerState CreateInstallerState(InstallerArguments arguments, InstallerState? previous)
{
    var tailscaleAuthKeyHash = string.IsNullOrWhiteSpace(arguments.TailscaleAuthKey)
        ? previous?.TailscaleAuthKeyHash ?? string.Empty
        : ComputeSha256(arguments.TailscaleAuthKey);
    var rustDeskPasswordHash = string.IsNullOrWhiteSpace(arguments.RustDeskPassword)
        ? previous?.RustDeskPasswordHash ?? string.Empty
        : ComputeSha256(arguments.RustDeskPassword);

    return new InstallerState(
        arguments.ServerUrl,
        arguments.DeviceName,
        arguments.RegistrationSharedKey,
        arguments.EnableAutoApprove,
        arguments.EnableRdp,
        arguments.CreateServiceUser,
        arguments.ServiceUserIsAdministrator,
        arguments.ServiceUserName,
        arguments.RustDeskId,
        arguments.TailscaleIpAddresses.ToArray(),
        tailscaleAuthKeyHash,
        rustDeskPasswordHash,
        true,
        DateTimeOffset.UtcNow);
}

static string GetInstallerStatePath()
{
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "StevensSupportHelper",
        "installer-state.json");
}

static bool ShouldSkipTailscaleAuth(InstallerState? state, string tailscaleAuthKey)
{
    return state is not null &&
           string.Equals(state.TailscaleAuthKeyHash, ComputeSha256(tailscaleAuthKey), StringComparison.Ordinal) &&
           DetectTailscaleIpAddresses().Count > 0;
}

static bool ShouldSkipRustDeskPassword(InstallerState? state, string rustDeskPassword)
{
    return state is not null &&
           string.Equals(state.RustDeskPasswordHash, ComputeSha256(rustDeskPassword), StringComparison.Ordinal);
}

static bool ShouldSkipRustDeskDirectAccess(InstallerState? state)
{
    return state?.RustDeskDirectAccessConfigured == true && IsRustDeskDirectAccessConfigured();
}

static bool ShouldSkipRemoteDesktop(InstallerState? state, InstallerArguments arguments)
{
    if (!arguments.EnableRdp)
    {
        return true;
    }

    var edition = GetWindowsEdition();
    if (edition.Contains("Home", StringComparison.OrdinalIgnoreCase))
    {
        return state?.EnableRdp == true;
    }

    return IsRdpEnabled();
}

static bool IsRdpEnabled()
{
    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
    var denyConnections = key?.GetValue("fDenyTSConnections");
    return denyConnections is int intValue && intValue == 0;
}

static bool IsRustDeskDirectAccessConfigured()
{
    string[] candidatePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RustDesk", "config", "RustDesk2.toml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk", "config", "RustDesk2.toml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustDesk", "config", "RustDesk2.toml")
    ];

    foreach (var candidatePath in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(candidatePath))
        {
            continue;
        }

        var content = File.ReadAllText(candidatePath);
        if (content.IndexOf("direct-server = \"Y\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
    }

    return false;
}

static bool IsWinRmHttpsConfigured()
{
    var listenerResult = RunProcess("winrm.cmd", "enumerate winrm/config/listener", throwOnFailure: false, timeoutMilliseconds: 60000);
    var combined = $"{listenerResult.StandardOutput}{Environment.NewLine}{listenerResult.StandardError}";
    return combined.IndexOf("Transport = HTTPS", StringComparison.OrdinalIgnoreCase) >= 0 ||
           combined.IndexOf("Transport=HTTPS", StringComparison.OrdinalIgnoreCase) >= 0;
}

static string ComputeSha256(string value)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes);
}

static void EnsureAdminUser(InstallerArguments arguments)
{
    if (!arguments.CreateServiceUser)
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(arguments.ServiceUserName) || string.IsNullOrWhiteSpace(arguments.ServiceUserPassword))
    {
        throw new InvalidOperationException("Service user creation is enabled, but username or password is missing.");
    }

    var escapedUserName = arguments.ServiceUserName.Replace("'", "''", StringComparison.Ordinal);
    var escapedPassword = arguments.ServiceUserPassword.Replace("'", "''", StringComparison.Ordinal);
    var script =
$@"$ErrorActionPreference = 'Stop'
$userName = '{escapedUserName}'
$localPrincipal = '{Environment.MachineName}\\{escapedUserName}'
$password = ConvertTo-SecureString '{escapedPassword}' -AsPlainText -Force
$localUser = Get-LocalUser -Name $userName -ErrorAction SilentlyContinue
if (-not $localUser) {{
    New-LocalUser -Name $userName -Password $password -PasswordNeverExpires -AccountNeverExpires:$true | Out-Null
}}
else {{
    Set-LocalUser -Name $userName -Password $password
}}
$localUser = Get-LocalUser -Name $userName -ErrorAction Stop
$localUserSid = $localUser.SID.Value
$memberCandidates = @($userName, $localPrincipal, '.\' + $userName) | Select-Object -Unique

function Get-GroupNameCandidates([string]$sidValue, [string[]]$fallbackNames) {{
    $candidates = [System.Collections.Generic.List[string]]::new()
    try {{
        $translatedName = ([System.Security.Principal.SecurityIdentifier]::new($sidValue)).Translate([System.Security.Principal.NTAccount]).Value.Split('\')[-1]
        if (-not [string]::IsNullOrWhiteSpace($translatedName)) {{
            $candidates.Add($translatedName)
        }}
    }}
    catch {{
    }}

    foreach ($fallbackName in $fallbackNames) {{
        if (-not [string]::IsNullOrWhiteSpace($fallbackName)) {{
            $candidates.Add($fallbackName)
        }}
    }}

    return $candidates | Select-Object -Unique
}}

function Test-LocalGroupContainsMember([string]$groupName) {{
    try {{
        return (@(Get-LocalGroupMember -Group $groupName -ErrorAction Stop | Where-Object {{
            ($_.SID -and $_.SID.Value -eq $localUserSid) -or
            ($memberCandidates -contains $_.Name)
        }}).Count -gt 0)
    }}
    catch {{
        return $false
    }}
}}

function Add-UserToLocalGroup([string]$sidValue, [string[]]$fallbackNames) {{
    $groupCandidates = Get-GroupNameCandidates -sidValue $sidValue -fallbackNames $fallbackNames
    foreach ($groupName in $groupCandidates) {{
        try {{
            $group = Get-LocalGroup -Name $groupName -ErrorAction Stop
        }}
        catch {{
            continue
        }}

        if (Test-LocalGroupContainsMember -groupName $group.Name) {{
            return $group.Name
        }}

        foreach ($memberCandidate in $memberCandidates) {{
            try {{
                Add-LocalGroupMember -Group $group.Name -Member $memberCandidate -ErrorAction Stop
            }}
            catch {{
                try {{
                    net localgroup ""$($group.Name)"" ""$memberCandidate"" /add | Out-Null
                }}
                catch {{
                    try {{
                        ([ADSI](""WinNT://./$($group.Name),group"")).Add(""WinNT://./$userName,user"")
                    }}
                    catch {{
                    }}
                }}
            }}

            if (Test-LocalGroupContainsMember -groupName $group.Name) {{
                return $group.Name
            }}
        }}
    }}

    return $null
}}

$usersGroupName = Add-UserToLocalGroup -sidValue 'S-1-5-32-545' -fallbackNames @('Users', 'Benutzer')
if ([string]::IsNullOrWhiteSpace($usersGroupName)) {{
    throw ('Failed to add local user ''{0}'' to the local Users group.' -f $localPrincipal)
}}

if ({(arguments.ServiceUserIsAdministrator ? "$true" : "$false")}) {{
    $administratorsGroupName = Add-UserToLocalGroup -sidValue 'S-1-5-32-544' -fallbackNames @('Administrators', 'Administratoren')
    if ([string]::IsNullOrWhiteSpace($administratorsGroupName)) {{
        throw ('Failed to add local user ''{0}'' to the local Administrators group.' -f $localPrincipal)
    }}

    $remoteManagementUsersGroupName = Add-UserToLocalGroup -sidValue 'S-1-5-32-580' -fallbackNames @('Remote Management Users', 'Remoteverwaltungsbenutzer', 'Benutzer der Remoteverwaltung')
    if ([string]::IsNullOrWhiteSpace($remoteManagementUsersGroupName)) {{
        throw ('Failed to add local user ''{0}'' to the local Remote Management Users group.' -f $localPrincipal)
    }}
}}";

    var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    RunProcess(
        "powershell.exe",
        $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
        throwOnFailure: true);
}

static void EnableWinRmRemoting()
{
    var script =
@"$ErrorActionPreference = 'Stop'
Enable-PSRemoting -Force | Out-Null
Set-Service -Name WinRM -StartupType Automatic
Start-Service -Name WinRM

$policyPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
if (-not (Test-Path $policyPath)) {
    New-Item -Path $policyPath -Force | Out-Null
}
New-ItemProperty -Path $policyPath -Name 'LocalAccountTokenFilterPolicy' -PropertyType DWord -Value 1 -Force | Out-Null

$dnsName = [System.Net.Dns]::GetHostEntry('localhost').HostName
if ([string]::IsNullOrWhiteSpace($dnsName)) {
    $dnsName = $env:COMPUTERNAME
}

$certificate = Get-ChildItem -Path Cert:\LocalMachine\My |
    Where-Object {
        $_.HasPrivateKey -and
        $_.Subject -match ('CN=' + [Regex]::Escape($dnsName)) -and
        $_.NotAfter -gt (Get-Date).AddDays(30)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -DnsName $dnsName, $env:COMPUTERNAME, 'localhost' `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -FriendlyName 'StevensSupportHelper WinRM HTTPS' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears(5)
}

$listenerPath = 'winrm/config/Listener?Address=*+Transport=HTTPS'
try {
    winrm delete $listenerPath | Out-Null
}
catch {
}

$listenerValue = '@{Hostname=""' + $dnsName + '""; CertificateThumbprint=""' + $certificate.Thumbprint + '""}'
winrm create $listenerPath $listenerValue | Out-Null

if (-not (Get-NetFirewallRule -DisplayName 'StevensSupportHelper WinRM HTTPS 5986' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'StevensSupportHelper WinRM HTTPS 5986' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5986 | Out-Null
}";

    var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    RunProcess(
        "powershell.exe",
        $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
        throwOnFailure: true);
}

static string? ResolveWingetPath()
{
    var direct = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
    if (File.Exists(direct))
    {
        return direct;
    }

    var commandResult = RunProcess("where.exe", "winget", throwOnFailure: false);
    if (commandResult.ExitCode == 0)
    {
        var candidate = commandResult.StandardOutput
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static void PrepareExistingInstall(InstallerArguments arguments)
{
    StopTrayProcesses();

    if (ServiceExists(arguments.ServiceName))
    {
        RunSc("stop", arguments.ServiceName, allowKnownStopErrors: true);
        WaitForServiceStatus(arguments.ServiceName, "Stopped", TimeSpan.FromSeconds(20), allowMissingService: false, allowStoppedService: true);
    }
}

static void StopTrayProcesses()
{
    foreach (var processName in new[] { "StevensSupportHelper.Client.Tray", "StevensSupportHelper.Client.Tray.exe" })
    {
        foreach (var process in Process.GetProcessesByName(processName.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }
}

static void CopyDirectoryWithTrayRetry(string sourceDirectory, string destinationDirectory)
{
    const int maxAttempts = 3;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            if (attempt > 1)
            {
                LogStep($"Retrying tray payload copy (attempt {attempt}/{maxAttempts}).");
            }

            CopyDirectory(sourceDirectory, destinationDirectory);
            return;
        }
        catch (IOException exception) when (attempt < maxAttempts)
        {
            LogStep($"Tray payload copy failed due to file lock: {exception.Message}");
            StopTrayProcesses();
            Thread.Sleep(1500);
        }
    }

    CopyDirectory(sourceDirectory, destinationDirectory);
}

static void TryRestartExistingService(InstallerArguments arguments)
{
    try
    {
        var serviceExecutable = Path.Combine(arguments.InstallRoot, "client-service", "StevensSupportHelper.Client.Service.exe");
        if (File.Exists(serviceExecutable))
        {
            LogStep("Install failed after stopping the service. Attempting to restore service startup.");
            InstallOrReplaceService(arguments.ServiceName, serviceExecutable, ServiceCredentials.LocalSystem);
        }

        var trayExecutable = Path.Combine(arguments.InstallRoot, "client-tray", "StevensSupportHelper.Client.Tray.exe");
        if (File.Exists(trayExecutable))
        {
            LogStep("Attempting to restart the previously installed tray after failed install.");
            StartTrayProcess(trayExecutable);
        }
    }
    catch (Exception restartException)
    {
        LogStep($"Failed to restart existing client components after install failure: {restartException}");
    }
}

static void CopyDirectory(string sourceDirectory, string destinationDirectory, IReadOnlyCollection<string>? excludedRelativePaths = null)
{
    if (!Directory.Exists(sourceDirectory))
    {
        throw new DirectoryNotFoundException($"Required payload directory not found: {sourceDirectory}");
    }

    Directory.CreateDirectory(destinationDirectory);

    foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
    }

    foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        var relativePath = Path.GetRelativePath(sourceDirectory, file);
        if (excludedRelativePaths is not null &&
            excludedRelativePaths.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
        {
            LogStep($"Skipping existing file during copy: {relativePath}");
            continue;
        }

        var destinationPath = Path.Combine(destinationDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(file, destinationPath, overwrite: true);
    }
}

static void UpdateServiceSettings(
    string settingsPath,
    string serverUrl,
    string deviceName,
    string registrationSharedKey,
    bool enableAutoApprove,
    string rustDeskId,
    IReadOnlyList<string> tailscaleIpAddresses)
{
    using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
    using var memoryStream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.NameEquals("StevensSupportHelper"))
            {
                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var child in property.Value.EnumerateObject())
                {
                    if (child.NameEquals("ServerBaseUrl"))
                    {
                        writer.WriteString(child.Name, serverUrl);
                    }
                    else if (child.NameEquals("DeviceName"))
                    {
                        writer.WriteString(child.Name, deviceName);
                    }
                    else if (child.NameEquals("RegistrationSharedKey"))
                    {
                        writer.WriteString(child.Name, registrationSharedKey);
                    }
                    else if (child.NameEquals("AutoApproveSupportRequests"))
                    {
                        writer.WriteBoolean(child.Name, enableAutoApprove);
                    }
                    else if (child.NameEquals("RustDeskId"))
                    {
                        writer.WriteString(child.Name, rustDeskId);
                    }
                    else if (child.NameEquals("TailscaleIpAddresses"))
                    {
                        writer.WritePropertyName(child.Name);
                        writer.WriteStartArray();
                        foreach (var address in tailscaleIpAddresses)
                        {
                            writer.WriteStringValue(address);
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        child.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            else
            {
                property.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }

    File.WriteAllBytes(settingsPath, memoryStream.ToArray());
}

static void WriteTraySettings(string serverUrl, string trayDestination)
{
    var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StevensSupportHelper");
    Directory.CreateDirectory(programDataRoot);
    var updatesRoot = Path.Combine(programDataRoot, "Updates");
    var traySettingsJson = JsonSerializer.Serialize(
        new
        {
            ServerBaseUrl = serverUrl,
            UpdatesRoot = updatesRoot
        },
        new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    var writtenPaths = new List<string>();
    foreach (var traySettingsPath in ResolveTraySettingsTargets(programDataRoot, trayDestination))
    {
        var targetDirectory = Path.GetDirectoryName(traySettingsPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            continue;
        }

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(traySettingsPath, traySettingsJson);
        if (!File.Exists(traySettingsPath))
        {
            throw new InvalidOperationException($"tray-settings.json could not be created at {traySettingsPath}.");
        }

        writtenPaths.Add(traySettingsPath);
    }

    if (writtenPaths.Count == 0)
    {
        throw new InvalidOperationException("No valid tray-settings.json target path was available.");
    }

    LogStep($"Wrote tray settings to {string.Join(", ", writtenPaths)}");
}

static IReadOnlyList<string> ResolveTraySettingsTargets(string programDataRoot, string trayDestination)
{
    var targets = new List<string>
    {
        Path.Combine(programDataRoot, "tray-settings.json")
    };

    if (!string.IsNullOrWhiteSpace(trayDestination))
    {
        targets.Add(Path.Combine(trayDestination, "tray-settings.json"));
    }

    return targets
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void InstallOrReplaceService(string serviceName, string executablePath, ServiceCredentials credentials)
{
    if (!File.Exists(executablePath))
    {
        throw new FileNotFoundException("Service executable was not found.", executablePath);
    }

    var accountClause = credentials.IsLocalSystem
        ? "obj= LocalSystem"
        : $"obj= \"{credentials.AccountName}\" password= \"{credentials.Password}\"";

    if (!ServiceExists(serviceName))
    {
        RunSc("create", $"{serviceName} binPath= \"\\\"{executablePath}\\\"\" start= auto {accountClause} DisplayName= \"StevensSupportHelper Client Service\"");
    }

    RunSc("description", $"{serviceName} \"StevensSupportHelper background agent for registration, heartbeats and managed support orchestration.\"");
    RunSc("config", $"{serviceName} binPath= \"\\\"{executablePath}\\\"\" start= delayed-auto {accountClause}");
    RunSc("failure", $"{serviceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000");
    RunSc("failureflag", $"{serviceName} 1");
    RunSc("start", serviceName);
    WaitForServiceStatus(serviceName, "Running", TimeSpan.FromSeconds(20), allowMissingService: false, allowStoppedService: false);
}

static bool ServiceExists(string serviceName)
{
    return RunProcess("sc.exe", $"query \"{serviceName}\"", throwOnFailure: false).ExitCode == 0;
}

static void WaitForServiceStatus(string serviceName, string desiredStatus, TimeSpan timeout, bool allowMissingService, bool allowStoppedService)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        var result = RunProcess("sc.exe", $"query \"{serviceName}\"", throwOnFailure: false);
        if (result.ExitCode != 0)
        {
            if (allowMissingService)
            {
                return;
            }
        }
        else
        {
            if (result.StandardOutput.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                result.StandardOutput.IndexOf(desiredStatus, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            if (allowStoppedService &&
                desiredStatus.Equals("Stopped", StringComparison.OrdinalIgnoreCase) &&
                result.StandardOutput.IndexOf("STOPPED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }
        }

        Thread.Sleep(500);
    }

    throw new InvalidOperationException($"Timed out waiting for service '{serviceName}' to reach status '{desiredStatus}'.");
}

static void InstallTrayStartup(string trayExecutablePath)
{
    if (!File.Exists(trayExecutablePath))
    {
        throw new FileNotFoundException("Tray executable was not found.", trayExecutablePath);
    }

    var startupDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
    Directory.CreateDirectory(startupDirectory);

    var legacyCommandPath = Path.Combine(startupDirectory, "StevensSupportHelper Client Tray.cmd");
    if (File.Exists(legacyCommandPath))
    {
        File.Delete(legacyCommandPath);
    }

    var shellType = Type.GetTypeFromProgID("WScript.Shell")
        ?? throw new InvalidOperationException("WScript.Shell COM automation is not available.");
    dynamic shell = Activator.CreateInstance(shellType)
        ?? throw new InvalidOperationException("Unable to instantiate WScript.Shell.");
    var shortcutPath = Path.Combine(startupDirectory, "StevensSupportHelper Client Tray.lnk");
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = trayExecutablePath;
    shortcut.WorkingDirectory = Path.GetDirectoryName(trayExecutablePath);
    shortcut.WindowStyle = 1;
    shortcut.Description = "Launch StevensSupportHelper Client Tray at user sign-in.";
    shortcut.Save();

    using var runKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true)
        ?? throw new InvalidOperationException("Unable to open HKLM Run key for tray autostart.");
    runKey.SetValue(
        "StevensSupportHelperClientTray",
        $"\"{trayExecutablePath}\"",
        RegistryValueKind.String);

    TryInstallTrayScheduledTask(trayExecutablePath);
}

static void TryInstallTrayScheduledTask(string trayExecutablePath)
{
    try
    {
        var currentUser = WindowsIdentity.GetCurrent().Name;
        if (string.IsNullOrWhiteSpace(currentUser) ||
            string.Equals(currentUser, @"NT AUTHORITY\SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            LogStep("Skipping tray scheduled task creation because no interactive installer user is available.");
            return;
        }

        const string taskName = "StevensSupportHelper Client Tray";
        var escapedExecutablePath = trayExecutablePath.Replace("\"", "\"\"", StringComparison.Ordinal);

        RunProcess(
            "schtasks.exe",
            $"/Delete /TN \"{taskName}\" /F",
            throwOnFailure: false,
            timeoutMilliseconds: 30000);

        var createArguments =
            $"/Create /TN \"{taskName}\" /SC ONLOGON /RL LIMITED /F /TR \"\\\"{escapedExecutablePath}\\\"\" /RU \"{currentUser}\"";
        var result = RunProcess(
            "schtasks.exe",
            createArguments,
            throwOnFailure: false,
            timeoutMilliseconds: 30000);

        if (result.ExitCode == 0)
        {
            LogStep($"Installed tray scheduled task for {currentUser}.");
            return;
        }

        LogStep($"Tray scheduled task could not be created. Startup shortcut and HKLM Run remain active. schtasks output: {SummarizeProcessOutput(result.StandardOutput, result.StandardError)}");
    }
    catch (Exception exception)
    {
        LogStep($"Tray scheduled task setup failed: {exception.Message}");
    }
}

static void StartTrayProcess(string trayExecutablePath)
{
    if (!File.Exists(trayExecutablePath))
    {
        return;
    }

    if (Process.GetProcessesByName("StevensSupportHelper.Client.Tray").Any())
    {
        LogStep("Tray application is already running. Skipping start.");
        return;
    }

    try
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = trayExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(trayExecutablePath),
            UseShellExecute = true
        });
    }
    catch
    {
    }
}

static string? DetectRustDeskId(string? configuredRustDeskId)
{
    if (!string.IsNullOrWhiteSpace(configuredRustDeskId))
    {
        return configuredRustDeskId.Trim();
    }

    string[] candidatePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RustDesk", "config", "RustDesk2.toml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustDesk", "config", "RustDesk2.toml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RustDesk", "config", "RustDesk2.toml")
    ];

    foreach (var candidatePath in candidatePaths.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(candidatePath))
        {
            continue;
        }

        string content = File.ReadAllText(candidatePath);
        var match = Regex.Match(content, @"(?m)^\s*id\s*=\s*['""]?(?<id>[0-9\-]+)['""]?\s*$");
        if (match.Success)
        {
            return match.Groups["id"].Value.Trim();
        }
    }

    return null;
}

static IReadOnlyList<string> DetectTailscaleIpAddresses()
{
    var addresses = NetworkInterface.GetAllNetworkInterfaces()
        .Where(static nic =>
            nic.OperationalStatus == OperationalStatus.Up &&
            (nic.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
             || nic.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)))
        .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
        .Select(static address => address.Address)
        .Where(static address =>
            !IPAddress.IsLoopback(address) &&
            !(address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv6LinkLocal))
        .Select(static address => address.ToString())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (addresses.Length > 0)
    {
        return addresses;
    }

    var tailscaleCli = ResolveTailscaleExecutable();
    if (tailscaleCli is null)
    {
        return [];
    }

    var cliAddresses = new List<string>();
    cliAddresses.AddRange(ReadCliAddresses(tailscaleCli, "ip -4"));
    cliAddresses.AddRange(ReadCliAddresses(tailscaleCli, "ip -6"));
    return cliAddresses
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IReadOnlyList<string> ReadCliAddresses(string executablePath, string arguments)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return [];
        }

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(4000);
        if (process.ExitCode != 0)
        {
            return [];
        }

        return output
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }
    catch
    {
        return [];
    }
}

static string? ResolveTailscaleExecutable()
{
    string[] candidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tailscale", "tailscale.exe")
    ];

    return candidates.FirstOrDefault(File.Exists);
}

static string? ResolveRustDeskExecutable()
{
    string[] candidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RustDesk", "RustDesk.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RustDesk", "RustDesk.exe")
    ];

    return candidates.FirstOrDefault(File.Exists);
}

static string? ReadSidecarValue(string fileName)
{
    var candidatePath = Path.Combine(AppContext.BaseDirectory, fileName);
    if (!File.Exists(candidatePath))
    {
        return null;
    }

    var value = File.ReadAllText(candidatePath).Trim();
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

static IReadOnlyList<string> ParseIpList(string input)
{
    return input
        .Split([",", ";", "\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void RunSc(string command, string arguments, bool allowKnownStopErrors = false)
{
    var result = RunProcess("sc.exe", $"{command} {arguments}", throwOnFailure: false);
    if (result.ExitCode == 0)
    {
        return;
    }

    if (allowKnownStopErrors &&
        result.StandardOutput.IndexOf("1062", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        return;
    }

    throw new InvalidOperationException($"sc.exe {command} failed: {result.StandardOutput}{Environment.NewLine}{result.StandardError}".Trim());
}

static ProcessResult RunProcess(string fileName, string arguments, bool throwOnFailure = true, int timeoutMilliseconds = 300000)
{
    LogStep($"Run: {FormatCommandForLog(fileName, arguments)}");
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Unable to start process {fileName}.");
    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
    var standardErrorTask = process.StandardError.ReadToEndAsync();

    if (!process.WaitForExit(timeoutMilliseconds))
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        throw new InvalidOperationException($"{fileName} did not finish within {TimeSpan.FromMilliseconds(timeoutMilliseconds):g}. Command: {arguments}");
    }

    Task.WaitAll(standardOutputTask, standardErrorTask);
    var standardOutput = standardOutputTask.Result;
    var standardError = standardErrorTask.Result;
    process.WaitForExit();

    if (!string.IsNullOrWhiteSpace(standardOutput))
    {
        Console.WriteLine(standardOutput.Trim());
    }

    if (!string.IsNullOrWhiteSpace(standardError))
    {
        Console.WriteLine(standardError.Trim());
    }

    LogStep($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}");

    if (throwOnFailure && process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{FormatCommandForLog(fileName, arguments)} failed: {SummarizeProcessOutput(standardOutput, standardError)}".Trim());
    }

    return new ProcessResult(process.ExitCode, standardOutput, standardError);
}

static string FormatCommandForLog(string fileName, string arguments)
{
    if (fileName.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
        arguments.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase))
    {
        return $"{fileName} -EncodedCommand <hidden>";
    }

    return $"{fileName} {arguments}";
}

static string SummarizeProcessOutput(string standardOutput, string standardError)
{
    var combined = string.Join(
        Environment.NewLine,
        new[] { standardOutput, standardError }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim()));

    if (string.IsNullOrWhiteSpace(combined))
    {
        return "No output was captured.";
    }

    combined = Regex.Replace(combined, "<[^>]+>", " ", RegexOptions.Singleline);
    combined = Regex.Replace(combined, @"\s+", " ").Trim();
    return combined.Length <= 600 ? combined : combined[..600] + "...";
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal sealed record InstallerArguments(
    string ServerUrl,
    string InstallRoot,
    string ServiceName,
    string DeviceName,
    string RegistrationSharedKey,
    bool InstallRustDesk,
    bool InstallTailscale,
    string RustDeskInstallerFileName,
    string TailscaleInstallerFileName,
    string TailscaleAuthKey,
    bool EnableAutoApprove,
    bool EnableRdp,
    bool CreateServiceUser,
    bool ServiceUserIsAdministrator,
    string ServiceUserName,
    string ServiceUserPassword,
    string RustDeskId,
    string RustDeskPassword,
    IReadOnlyList<string> TailscaleIpAddresses,
    bool Silent,
    bool UpdateOnly)
{
    private const string DefaultServerUrl = "http://localhost:5000";
    private const string DefaultInstallRoot = @"C:\Program Files\StevensSupportHelper";
    private const string DefaultServiceName = "StevensSupportHelperClientService";

    public static InstallerArguments Parse(string[] args)
    {
        var config = LoadSidecarConfig();
        var serverUrl = config.ServerUrl ?? DefaultServerUrl;
        var serverSidecarPath = Path.Combine(AppContext.BaseDirectory, "server.txt");
        if (File.Exists(serverSidecarPath))
        {
            var sidecarServerUrl = File.ReadAllText(serverSidecarPath).Trim();
            if (!string.IsNullOrWhiteSpace(sidecarServerUrl))
            {
                serverUrl = sidecarServerUrl;
            }
        }
        var installRoot = config.InstallRoot ?? DefaultInstallRoot;
        var serviceName = config.ServiceName ?? DefaultServiceName;
        var deviceName = config.DeviceName ?? Environment.MachineName;
        var registrationSharedKey = config.RegistrationSharedKey ?? string.Empty;
        var installRustDesk = config.InstallRustDesk;
        var installTailscale = config.InstallTailscale;
        var rustDeskInstallerFileName = config.RustDeskInstallerFileName ?? string.Empty;
        var tailscaleInstallerFileName = config.TailscaleInstallerFileName ?? string.Empty;
        var tailscaleAuthKey = config.TailscaleAuthKey ?? string.Empty;
        var enableAutoApprove = config.EnableAutoApprove;
        var enableRdp = config.EnableRdp;
        var createServiceUser = config.CreateServiceUser;
        var serviceUserIsAdministrator = config.ServiceUserIsAdministrator;
        var serviceUserName = config.ServiceUserName ?? string.Empty;
        var serviceUserPassword = config.ServiceUserPassword ?? string.Empty;
        var rustDeskId = config.RustDeskId ?? string.Empty;
        var rustDeskPassword = config.RustDeskPassword ?? string.Empty;
        IReadOnlyList<string> tailscaleIpAddresses = config.TailscaleIpAddresses ?? [];
        var silent = config.Silent;
        var updateOnly = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--server-url":
                    serverUrl = GetValue(args, ref index);
                    break;
                case "--install-root":
                    installRoot = GetValue(args, ref index);
                    break;
                case "--service-name":
                    serviceName = GetValue(args, ref index);
                    break;
                case "--device-name":
                    deviceName = GetValue(args, ref index);
                    break;
                case "--registration-key":
                case "--api-key":
                    registrationSharedKey = GetValue(args, ref index);
                    break;
                case "--install-rustdesk":
                    installRustDesk = true;
                    break;
                case "--install-tailscale":
                    installTailscale = true;
                    break;
                case "--rustdesk-installer-file":
                    rustDeskInstallerFileName = GetValue(args, ref index);
                    break;
                case "--tailscale-installer-file":
                    tailscaleInstallerFileName = GetValue(args, ref index);
                    break;
                case "--tailscale-auth-key":
                    tailscaleAuthKey = GetValue(args, ref index);
                    break;
                case "--auto-approve":
                    enableAutoApprove = true;
                    break;
                case "--enable-rdp":
                    enableRdp = true;
                    break;
                case "--create-service-user":
                    createServiceUser = true;
                    break;
                case "--service-user-is-administrator":
                    serviceUserIsAdministrator = true;
                    break;
                case "--service-user-name":
                    serviceUserName = GetValue(args, ref index);
                    break;
                case "--service-user-password":
                    serviceUserPassword = GetValue(args, ref index);
                    break;
                case "--rustdesk-id":
                    rustDeskId = GetValue(args, ref index);
                    break;
                case "--rustdesk-password":
                    rustDeskPassword = GetValue(args, ref index);
                    break;
                case "--tailscale-ips":
                    tailscaleIpAddresses = GetValue(args, ref index)
                        .Split([",", ";", "\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    break;
                case "--silent":
                    silent = true;
                    break;
                case "--update-only":
                    updateOnly = true;
                    break;
                default:
                    throw new InvalidOperationException("Unknown argument '" + args[index] + "'. Supported: --server-url, --install-root, --service-name, --device-name, --registration-key, --api-key, --install-rustdesk, --install-tailscale, --rustdesk-installer-file, --tailscale-installer-file, --tailscale-auth-key, --auto-approve, --enable-rdp, --create-service-user, --service-user-is-administrator, --service-user-name, --service-user-password, --rustdesk-id, --rustdesk-password, --tailscale-ips, --silent, --update-only");
            }
        }

        return new InstallerArguments(serverUrl, installRoot, serviceName, deviceName, registrationSharedKey, installRustDesk, installTailscale, rustDeskInstallerFileName, tailscaleInstallerFileName, tailscaleAuthKey, enableAutoApprove, enableRdp, createServiceUser, serviceUserIsAdministrator, serviceUserName, serviceUserPassword, rustDeskId, rustDeskPassword, tailscaleIpAddresses, silent, updateOnly);
    }

    private static string GetValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for argument '{args[index]}'.");
        }

        index++;
        return args[index];
    }

    private static InstallerSidecarConfig LoadSidecarConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "client.installer.config");
        if (!File.Exists(path))
        {
            return new InstallerSidecarConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<InstallerSidecarConfig>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? new InstallerSidecarConfig();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"client.installer.config is invalid JSON: {exception.Message}");
        }
    }
}

internal sealed record InstallerSidecarConfig(
    string? ServerUrl = null,
    string? InstallRoot = null,
    string? ServiceName = null,
    string? DeviceName = null,
    string? RegistrationSharedKey = null,
    bool InstallRustDesk = false,
    bool InstallTailscale = false,
    string? RustDeskInstallerFileName = null,
    string? TailscaleInstallerFileName = null,
    string? TailscaleAuthKey = null,
    bool EnableAutoApprove = false,
    bool EnableRdp = false,
    bool CreateServiceUser = false,
    bool ServiceUserIsAdministrator = true,
    string? ServiceUserName = null,
    string? ServiceUserPassword = null,
    string? RustDeskId = null,
    string? RustDeskPassword = null,
    IReadOnlyList<string>? TailscaleIpAddresses = null,
    bool Silent = false);

internal sealed record ServiceCredentials(string AccountName, string Password)
{
    public static ServiceCredentials LocalSystem { get; } = new("LocalSystem", string.Empty);
    public bool IsLocalSystem => string.Equals(AccountName, "LocalSystem", StringComparison.OrdinalIgnoreCase);
}

internal sealed record InstallerState(
    string ServerUrl,
    string DeviceName,
    string RegistrationSharedKey,
    bool EnableAutoApprove,
    bool EnableRdp,
    bool CreateServiceUser,
    bool ServiceUserIsAdministrator,
    string ServiceUserName,
    string RustDeskId,
    IReadOnlyList<string> TailscaleIpAddresses,
    string TailscaleAuthKeyHash,
    string RustDeskPasswordHash,
    bool RustDeskDirectAccessConfigured,
    DateTimeOffset LastAppliedAtUtc);

internal sealed record UpdateBackupContext(
    string BackupRoot,
    string ServiceBackupPath,
    string TrayBackupPath);

internal static class InstallerRuntimeContext
{
    public static string? InstallerLogPath { get; set; }
}
