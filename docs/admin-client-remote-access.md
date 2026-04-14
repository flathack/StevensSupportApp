# Admin Client Remote Access

## RDP

Der Admin-Client verwendet fuer RDP jetzt immer zuerst die erste gemeldete
Tailscale-IP des Clients. Nur wenn keine Tailscale-IP vorhanden ist, faellt er
auf den Maschinenamen zurueck.

## RustDesk

RustDesk verwendet bevorzugt die direkte IP/Tailscale-IP. Wenn keine direkte IP
vorliegt, wird die RustDesk-ID benutzt.

## WinRM

PowerShell, Task Manager und File Explorer arbeiten ueber WinRM HTTPS auf Port
`5986`. Der Client-Installer richtet dafuer:

- `Enable-PSRemoting`
- einen HTTPS-Listener
- eine Firewall-Regel fuer `5986`

ein.

## Agent-Version

Die vom Client gemeldete Agent-Version wird im Admin-Client in der Clientliste
angezeigt. Nach einem Client-Update oder einer Neuinstallation erscheint die neue
Version beim naechsten Heartbeat.
