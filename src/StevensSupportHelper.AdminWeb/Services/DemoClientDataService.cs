namespace StevensSupportHelper.AdminWeb.Services;

public sealed class DemoClientDataService
{
    private readonly List<ClientSummaryResponse> _clients;
    private readonly Dictionary<Guid, ClientDetailResponse> _details;
    private readonly List<RemoteActionResponse> _remoteActions;

    public DemoClientDataService()
    {
        var now = DateTimeOffset.UtcNow;

        _clients =
        [
            new ClientSummaryResponse(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Reception-PC",
                "ALPHA-RECEPTION",
                "reception.user",
                true,
                "1.5.2",
                now.AddMinutes(-2),
                "Empfangsarbeitsplatz mit täglichem Supportbedarf."),
            new ClientSummaryResponse(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Accounting-Laptop",
                "ALPHA-ACCT-07",
                "sabine.m",
                true,
                "1.5.2",
                now.AddMinutes(-7),
                "Finanzgerät mit offenem Druckerproblem."),
            new ClientSummaryResponse(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "Warehouse-Terminal",
                "ALPHA-WH-03",
                "logistics",
                false,
                "1.4.9",
                now.AddHours(-3),
                "Gemeinsames Terminal, seit dem Schichtwechsel offline.")
        ];

        _details = new Dictionary<Guid, ClientDetailResponse>
        {
            [_clients[0].ClientId] = new ClientDetailResponse(
                _clients[0].ClientId,
                _clients[0].DeviceName,
                _clients[0].MachineName,
                _clients[0].CurrentUser,
                true,
                false,
                _clients[0].AgentVersion,
                "Windows 11 Pro 24H2",
                now.AddDays(-5),
                _clients[0].IsOnline,
                true,
                true,
                ["100.92.10.12"],
                now.AddMonths(-4),
                _clients[0].LastSeenAtUtc,
                _clients[0].Notes,
                "RD-004-774",
                new SupportRequestResponse(Guid.Parse("aaaaaaa1-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), "Ausstehend", "Wartet auf Zustimmung des Benutzers."),
                null),
            [_clients[1].ClientId] = new ClientDetailResponse(
                _clients[1].ClientId,
                _clients[1].DeviceName,
                _clients[1].MachineName,
                _clients[1].CurrentUser,
                true,
                false,
                _clients[1].AgentVersion,
                "Windows 11 Enterprise 24H2",
                now.AddDays(-2),
                _clients[1].IsOnline,
                false,
                true,
                ["100.92.10.44"],
                now.AddMonths(-6),
                _clients[1].LastSeenAtUtc,
                _clients[1].Notes,
                "RD-991-220",
                null,
                new SupportSessionResponse(
                    Guid.Parse("bbbbbbb2-bbbb-bbbb-bbbb-bbbbbbbbbbb2"),
                    "Standard-Administrator",
                    "RustDesk",
                    now.AddMinutes(-18))),
            [_clients[2].ClientId] = new ClientDetailResponse(
                _clients[2].ClientId,
                _clients[2].DeviceName,
                _clients[2].MachineName,
                _clients[2].CurrentUser,
                false,
                true,
                _clients[2].AgentVersion,
                "Windows 10 IoT Enterprise",
                now.AddDays(-18),
                _clients[2].IsOnline,
                true,
                false,
                [],
                now.AddMonths(-10),
                _clients[2].LastSeenAtUtc,
                _clients[2].Notes,
                null,
                null,
                null)
        };

        _remoteActions =
        [
            new RemoteActionResponse("collect_support_snapshot.ps1", "Sammelt Diagnosedaten und eine kurze Systemübersicht.", false),
            new RemoteActionResponse("restart_spooler.ps1", "Startet den Windows-Druckspooler neu.", true),
            new RemoteActionResponse("winget_update_all.ps1", "Plant Anwendungsupdates per winget ein.", true)
        ];
    }

    public IReadOnlyList<ClientSummaryResponse> GetClients()
    {
        return _clients;
    }

    public ClientDetailResponse? GetClient(Guid clientId)
    {
        return _details.TryGetValue(clientId, out var detail) ? detail : null;
    }

    public IReadOnlyList<RemoteActionResponse> GetRemoteActions()
    {
        return _remoteActions;
    }
}
