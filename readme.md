# StevensSupportHelper

Windows-Remote-Support-Projekt mit sichtbarer, zustimmungsbasierter Fernwartung. Das Repo enthaelt Server, Client-Service, sichtbare Tray-App, Windows-Installer und eine Admin-Konsole fuer Support, Wartung und direkte Remote-Aktionen.

## Aktueller Stand

Der aktuelle Stand ist ein funktionsfaehiger Prototyp mit diesen Bausteinen:

- ASP.NET Core Server fuer Client-Registrierung, Heartbeats, Support-Anfragen, Sessions, Audit und Datei-Transfers
- persistente Serverspeicherung per SQLite unter `%ProgramData%\StevensSupportHelper\server-state.db`
- HMAC-signierte Client-Registrierung mit Shared Key, Nonce und Zeitpruefung
- API-Key-basierte Admin-Authentifizierung mit Rollen fuer `Administrator`, `Operator` und `Auditor`
- optionale TOTP-MFA fuer Admins ueber `X-Admin-Totp`
- Rate Limiting fuer Client-, Admin-Read- und Admin-Write-Endpunkte
- Windows Worker Service als Client-Agent mit Heartbeats und Versionsmeldung
- sichtbare Client-Tray-App mit Support-Freigabe per Approve/Deny
- Audit-Ansicht und Live-Clientliste in der Admin-Konsole
- Datei-Upload und Datei-Download innerhalb einer freigegebenen Session
- WinRM-basierte Remote-Werkzeuge in der Admin-Konsole:
  - PowerShell
  - Registry
  - Files
  - Tasks
  - Software
- direkte Connect-Optionen fuer RDP, RustDesk und WinRM
- Client-Installer fuer Erstinstallation und Update-Only-Szenarien
- Remote-Action-System auf Basis frei bearbeitbarer PowerShell-Skripte

## Was sich zuletzt geaendert hat

Wichtige aktuelle Aenderungen:

- neue Admin- und Client-Icons unter `assets/icons`
- Clientkarten im Admin-Client werden nebeneinander dargestellt
- Tabs im Admin-Client sind schliessbar
- Registry-Zugriffe wurden robuster gemacht
- `RDP Connect` ist im Admin-Client sichtbar
- Update-/Repair-Logik wurde aus festen Buttons in ein flexibleres Remote-Action-Modell ueberfuehrt
- Remote Actions koennen ueber einen konfigurierbaren Skriptordner geladen, bearbeitet und direkt auf einem Client ausgefuehrt werden
- Standardskripte fuer typische Admin-Aufgaben liegen bereits unter `publish/RemoteActions`

## Projektstruktur

- `src/StevensSupportHelper.Shared`
  Gemeinsame DTOs, Vertraege und Hilfslogik.
- `src/StevensSupportHelper.Server`
  Backend-API, Client-Registry, Persistierung und Admin-Authentifizierung.
- `src/StevensSupportHelper.Client.Service`
  Windows-Worker-Service fuer Registrierung, Heartbeats und Update-/Statuslogik.
- `src/StevensSupportHelper.Client.Tray`
  Sichtbare Windows-Tray-App mit Zustimmungsdialog.
- `src/StevensSupportHelper.Admin`
  WPF-Admin-Konsole mit Clientliste, Remote-Werkzeugen und Remote-Actions.
- `src/StevensSupportHelper.Installer`
  Windows-Installer fuer Erstinstallation und Update-/Repair-Szenarien.
- `scripts`
  Build-, Install-, Sign- und Dev-Helfer.
- `docs`
  Zusatzdokumentation fuer Installer und Admin-Remotezugriff.
- `publish/RemoteActions`
  Beispiel- und Standardskripte fuer das Remote-Action-System.

## Admin Client

Die Admin-Konsole bietet aktuell:

- Live-Clientliste mit Online-/Offline-Status, Agent-Version, Session-Status und Notizen
- Support-Anfrage mit sichtbarer Zustimmung auf dem Client
- direkte Verbindungen ueber:
  - RDP
  - RustDesk
  - WinRM
- Remote-Werkzeuge als Tabs:
  - PowerShell
  - Registry
  - Files
  - Tasks
  - Software
- Audit-Ansicht
- Log-Ansicht fuer lokale Admin-Aktionen
- Remote-Action-Fenster fuer frei bearbeitbare PowerShell-Skripte

### Remote Actions

Remote Actions sind der aktuelle Weg fuer flexible Fernwartung.

Funktion:

- per Button `Remote Action` oder per Rechtsklick auf einen Client
- Precheck auf WinRM-Erreichbarkeit und verfuegbare Credentials
- Auflistung aller `.ps1`-Skripte aus einem konfigurierbaren Skriptordner
- Skript vor dem Start editierbar
- Ausgabe und Fehler sichtbar in einem Status-/Output-Fenster

Konfigurierbar in den Admin-Settings:

- Server URL
- API Key
- Server-Projektpfad
- RustDesk-Pfad
- Client-Installer-Pfad
- Remote-Actions-Pfad
- globale Remote-Zugangsdaten

Standard-Skripte liegen unter:

- `publish/RemoteActions`

Beispiele:

- `remote_update_client.ps1`
- `check_client_service.ps1`
- `collect_support_snapshot.ps1`
- `show_network_state.ps1`
- `winget_update_all.ps1`

## Client Installer

Der Installer unter `src/StevensSupportHelper.Installer` unterstuetzt:

- Erstinstallation
- `--update-only`
- lokale Sidecar-Datei `client.installer.config`
- optionale Installation bzw. Vorbereitung von:
  - RustDesk
  - Tailscale
  - RDP
  - WinRM over HTTPS
- sichtbare Schritt-Logs im Konsolenfenster
- zusaetzliche Installer-Logs unter `%ProgramData%\StevensSupportHelper\InstallerLogs`

Wichtige Doku:

- `docs/client-installer-oneclick.md`

Wichtiger aktueller Punkt:

- Im `update-only`-Modus wird `appsettings.json` nicht mehr ueberschrieben. Es werden nur Dienst/Tray gestoppt, Dateien ersetzt und danach wieder gestartet.

## Remote Zugriff

Kurzueberblick:

- RDP verwendet bevorzugt die erste gemeldete Tailscale-IP des Clients
- RustDesk verwendet bevorzugt direkte IP bzw. Tailscale-IP, sonst die RustDesk-ID
- WinRM-Werkzeuge arbeiten ueber HTTPS auf Port `5986`

Wichtige Doku:

- `docs/admin-client-remote-access.md`

## Lokaler Start

Schnellster Weg fuer den kompletten lokalen Stack:

```powershell
.\scripts\start-dev.ps1
```

Beenden der gestarteten Prozesse:

```powershell
.\scripts\stop-dev.ps1
```

Einzelne Komponenten manuell:

1. Server:

```powershell
dotnet run --project .\src\StevensSupportHelper.Server\StevensSupportHelper.Server.csproj --launch-profile http
```

2. Client-Service:

```powershell
dotnet run --project .\src\StevensSupportHelper.Client.Service\StevensSupportHelper.Client.Service.csproj
```

3. Admin-Konsole:

```powershell
dotnet run --project .\src\StevensSupportHelper.Admin\StevensSupportHelper.Admin.csproj
```

4. Tray-App:

```powershell
dotnet run --project .\src\StevensSupportHelper.Client.Tray\StevensSupportHelper.Client.Tray.csproj
```

## Lokale Client-Installation

Fuer eine echte lokale Windows-Installation:

```powershell
.\scripts\install-client.ps1 -ServerUrl http://localhost:5000
```

Das Skript publiziert Service und Tray, richtet den Windows-Service ein und legt den Tray-Autostart an.

Nur Publish-Test ohne echte Installation:

```powershell
.\scripts\install-client.ps1 -PublishOnly -InstallRoot .\publish\client-test
```

Deinstallation:

```powershell
.\scripts\uninstall-client.ps1
```

## Build und Publish

Wichtige Skripte:

- `scripts/build-client-installer-exe.ps1`
  Baut den Client-Installer mit eingebettetem Payload-Bundle.
- `scripts/build-release-manifest.ps1`
  Baut ein Client-Bundle plus `release-manifest.json`.
- `scripts/build-msix-installer.ps1`
  Erzeugt eine MSIX-faehige Paketstruktur und optional eine echte `.msix`.
- `scripts/sign-release.ps1`
  Signierung fuer Release-Artefakte.

Aktuelle Publish-Beispiele im Repo:

- `publish/admin-1.5.0-test-remote-actions-20260327`
- `publish/client-installer-1.4.4-test-hotfix-20260327`
- `publish/RemoteActions`

## Konfiguration

Standard-Serveradresse:

- `http://localhost:5000`

Wichtige Server-Konfiguration:

- `src/StevensSupportHelper.Server/appsettings.json`

Dort konfigurierbar:

- SQLite-Pfad
- Session-Timeout
- Consent-Timeout
- Admin-Accounts
- MFA-Header
- Rate Limiting
- Shared Key fuer signierte Registrierungen

Wichtige Client-Konfiguration:

- `src/StevensSupportHelper.Client.Service/appsettings.json`

Dort konfigurierbar:

- `ServerBaseUrl`
- `RegistrationSharedKey`
- Update-Manifest-URL
- Update-Kanal
- Update-Intervall
- Update-Verzeichnis

## Logging

Standardmaessige Logs:

- `%ProgramData%\StevensSupportHelper\Logs`
- `%ProgramData%\StevensSupportHelper\InstallerLogs`

Die Admin-Konsole fuehrt zusaetzlich eine eigene Log-Ansicht fuer lokale Aktionen und Remote-Ausfuehrungen.

## Offene Punkte

Noch nicht als final produktionsreif zu betrachten:

- vollstaendig ausgereifter Update-/Rollback-Prozess fuer alle Edge Cases
- endgueltige produktive Installer-/Rollout-Strategie
- haertere Security-Defaults fuer echten Internetbetrieb
- weitergehende Automatisierung fuer Remote-Recovery und Remote-Deployment

## Weiterfuehrende Dateien

- `docs/client-installer-oneclick.md`
- `docs/admin-client-remote-access.md`
- `client.installer.config.sample`
- `SECURITY_REVIEW.md`
