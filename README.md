# FlatHack ServiceHub

FlatHack ServiceHub is a Windows remote-support toolkit for visible, consent-based assistance. It brings client status, support sessions, administrative tools, remote actions, file transfers, and audit information together in one operator console.

The project is currently a functional prototype. It is intended for controlled support environments and is not yet presented as a production-ready, internet-facing remote-management platform.

## What it provides

- a Windows client agent with heartbeat and version reporting
- a visible tray application with Approve/Deny consent for support requests
- an operator console with live client and session status
- connection workflows for RDP, RustDesk, and WinRM
- remote tools for PowerShell, files, registry, tasks, and installed software
- editable PowerShell-based Remote Actions for repeatable support work
- file transfer and audit records within approved sessions
- installer and update workflows for managed Windows clients

FlatHack ServiceHub coordinates these support workflows; remote access still relies on the configured Windows and third-party connection technologies.

## Public distribution repository

This repository is the public home for release information, the pilot Docker deployment and future distribution packages. The application source code and private deployment material are maintained separately and are not published here.

## Pilot Docker deployment

The pilot server image is published as `ghcr.io/flathack/flathack-servicehub:pilot` only after private Ubuntu, Windows, secret-scan and container-security gates pass. It supports `linux/amd64` and `linux/arm64`.

The pilot binds both ports to loopback. Put a TLS reverse proxy or a private overlay such as Tailscale in front of it; do not expose the raw HTTP ports directly to the internet.

```bash
git clone https://github.com/flathack/flathack-servicehub.git
cd flathack-servicehub
./scripts/bootstrap-pilot.sh
```

Open `http://127.0.0.1:5001/`, sign in with user `admin` and the locally generated password in `secrets/bootstrap-password.txt`, then immediately:

1. enroll MFA and store the recovery codes;
2. set `SERVICEHUB_BOOTSTRAP_ENABLED=false` in `.env.pilot`;
3. restart with `docker compose --env-file .env.pilot -f compose.pilot.yaml up -d`;
4. run `./scripts/backup-pilot.sh`, copy the backup off-host and set `SERVICEHUB_REQUIRE_RECENT_BACKUP=true`.

The moving `pilot` tag is for a controlled test fleet. Record the deployed image digest before every upgrade and use a version tag for a later production release.

## Security model

Remote support is designed around explicit client consent, authenticated administration, role separation, rate limiting, and auditable sessions. Production deployments should additionally use HTTPS, unique high-entropy secrets, an isolated network, and a reviewed client rollout process.

## Releases and Packages

GitHub remains the public endpoint for release-facing metadata, GHCR images and future signed agent packages. Agent installers are not published until the Windows Authenticode and update-manifest signing gates both pass.

## License

MIT License. See [LICENSE](LICENSE).
