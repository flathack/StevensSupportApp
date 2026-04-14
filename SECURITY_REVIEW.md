# StevensSupportHelper Security Review

## Ziel

Diese Checkliste bereitet einen Security-Review oder Penetration-Test gegen den aktuellen Prototyp vor. Sie deckt die wichtigsten Vorbedingungen, Prüfflächen und Artefakte ab, damit ein externer oder interner Reviewer nicht erst die Architektur rekonstruieren muss.

## Vor Review prüfen

- `scripts/security-review.ps1` ausführen und alle `FAIL`-Befunde vor dem Review schließen oder bewusst freigeben.
- Demo-Secrets in `src/StevensSupportHelper.Server/appsettings.json` und `src/StevensSupportHelper.Client.Service/appsettings.json` ersetzen.
- Sicherstellen, dass der Review-Build mit HTTPS statt reinem HTTP betrieben wird.
- Prüfen, dass `%ProgramData%\StevensSupportHelper\Logs` und Audit-Daten vor dem Test gesichert werden.
- Einen dedizierten Review-Mandanten oder eine isolierte Testumgebung verwenden.

## Review-Schwerpunkte

### Authentifizierung und Rollen

- Admin-Zugriff mit gültigem API-Key, ungültigem API-Key und Auditor-Rolle testen.
- Privilegtrennung zwischen `Auditor`, `Operator` und `Administrator` verifizieren.
- Prüfen, dass schreibende Admin-Endpunkte keine Auditoren akzeptieren.

### Client-Vertrauen und Registrierung

- Registrierungen mit fehlender, manipulierte oder veralteter Signatur testen.
- Clock-skew-Verhalten und Bootstrap-Key-Rotation prüfen.
- Re-Registrierung nach Server-Neustart und nach `401 Unauthorized` nachvollziehen.

### Sitzungsschutz und Zustimmung

- Consent-Workflow für Approve, Deny und Timeout verifizieren.
- Session-Timeout und Reaktion auf parallele Requests prüfen.
- Sicherstellen, dass nur eine aktive Session pro Client möglich ist.

### Dateioperationen

- Path Traversal, absolute Pfade und große Base64-Payloads testen.
- Upload- und Download-Pfade gegen `%ProgramData%\StevensSupportHelper\ManagedFiles` verifizieren.
- Audit-Einträge für Transfers und Fehlerfälle prüfen.

### Transport und API-Härtung

- HTTPS-Default und Redirect-Verhalten prüfen.
- Rate-Limits für Client-, Admin-Read- und Admin-Write-Endpunkte testen.
- Verhalten bei wiederholten Fehlern, Timeouts und Server-Neustarts prüfen.

### Betrieb und Forensik

- Crash- und Lifecycle-Logs unter `%ProgramData%\StevensSupportHelper\Logs` einsammeln.
- Audit-Historie und Log-Korrelation für Session- und Transferabläufe prüfen.
- Install-/Uninstall-Skripte auf privilegierte Dateisystem- und Service-Änderungen reviewen.

## Erwartete Artefakte für Reviewer

- Dieses Dokument
- [PROJECT_PLAN.md](C:/Users/steve/Github/StevensSupportHelper/PROJECT_PLAN.md)
- [readme.md](C:/Users/steve/Github/StevensSupportHelper/readme.md)
- Audit-Export aus der Admin-Konsole
- Crash-/Lifecycle-Logs unter `%ProgramData%\StevensSupportHelper\Logs`
- Ergebnis von `scripts/security-review.ps1`

## Offene Rest-Risiken vor Produktion

- Keine MFA für Admins
- JSON-Persistierung statt Produktionsdatenbank
- Kein Auto-Update
- Keine Code-Signierung
- Kein eigener Remote-Desktop-Kanal für nicht direkt erreichbare Systeme
