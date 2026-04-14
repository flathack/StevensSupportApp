# Client Installer One-Click

## Sidecar-Dateien

Lege diese Dateien neben `StevensSupportHelper.Installer.exe`:

- `client.installer.config`
`client.installer.config` ist JSON. Eine Vorlage liegt in
`client.installer.config.sample`.

`server.txt` und `tailscale.txt` werden weiterhin als Fallback gelesen, sind aber
nicht mehr noetig, wenn `serverUrl` und `tailscaleAuthKey` in der Config stehen.
Lokale RustDesk- und Tailscale-Setups neben der EXE koennen ebenfalls direkt ueber
die Config referenziert werden.

## Prioritaet der Konfiguration

1. CLI-Parameter
2. `client.installer.config`
3. `server.txt`
4. `tailscale.txt`
5. interne Defaults

## Beispiel

```text
C:\Software\StevensSupportHelper.Installer.exe
C:\Software\client.installer.config
```

Dann reicht:

```powershell
Start-Process -Verb RunAs .\StevensSupportHelper.Installer.exe
```

Wenn `silent=true` in der Config gesetzt ist, laeuft die Installation ohne Dialog.

## Neue Optionen

- `enableRdp`
  Aktiviert RDP auf Windows Pro/Enterprise und oeffnet die Firewall.
- `tailscaleAuthKey`
  Verbindet Tailscale direkt waehrend der Installation, ohne separate TXT-Datei.
- `rustDeskInstallerFileName`
  Dateiname eines lokalen RustDesk-Installers neben der EXE, z. B. `rustdesk-1.4.6-x86_64.msi`.
- `tailscaleInstallerFileName`
  Dateiname eines lokalen Tailscale-Installers neben der EXE, z. B. `tailscale-setup-1.96.3.exe`.
- `createServiceUser`
  Legt einen lokalen Benutzer an und startet den Client-Service unter diesem Konto.
- `serviceUserIsAdministrator`
  Fuegt den Service-User optional zusaetzlich zur lokalen Administratoren-Gruppe hinzu.
- `serviceUserName`
  Name des lokalen Dienstkontos.
- `serviceUserPassword`
  Kennwort des lokalen Dienstkontos.

## WinRM fuer unbeaufsichtigte Clients

Der Installer richtet WinRM over HTTPS auf Port `5986` ein und setzt
`LocalAccountTokenFilterPolicy=1`. Das ist fuer lokale Administrator-Konten
wichtig, damit sie auch am Logon-Screen per WinRM mit vollem Admin-Token
arbeiten koennen.

Wenn `serviceUserIsAdministrator=true` gesetzt ist, fuegt der Installer den
zusaetzlichen lokalen Benutzer ausserdem zur eingebauten Gruppe
`Remote Management Users` hinzu.

## Hinweis zu Windows Home

Wenn `enableRdp=true` auf Windows Home gesetzt ist, ueberspringt der Installer
die RDP-Aktivierung. Auf Windows Pro/Enterprise wird RDP weiterhin aktiviert und
die Firewall geoeffnet.

## Sichtbare Install-Schritte

Auch bei `silent=true` bleibt das Konsolenfenster aktiv. Dort schreibt der
Installer jeden groesseren Schritt mit Zeitstempel mit, damit Netzwerk- oder
Paketprobleme bei RustDesk und Tailscale leichter sichtbar sind.

Wenn `rustDeskInstallerFileName` oder `tailscaleInstallerFileName` gesetzt sind,
bevorzugt der Installer diese lokalen Dateien neben der EXE vor `winget`.

PowerShell-Hilfsschritte fuer RDP, Service-User und WinRM laufen intern ueber
`powershell.exe -EncodedCommand ...`. Das ist nur Base64-kodierter Skripttext,
keine Verschluesselung. Der Installer startet diese Befehle jetzt explizit
`-NonInteractive` und mit Timeout, damit haengende Shells sauber als Fehler
sichtbar werden.
