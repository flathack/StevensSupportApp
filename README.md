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

This repository is the public home for release information and future distribution packages. The application source code and private deployment material are maintained separately and are not published here.

## New build in progress

The former container image and its deployment examples have been retired. A newly designed FlatHack ServiceHub build and distribution workflow will replace them. There is currently no supported public container image or deployment package.

## Security model

Remote support is designed around explicit client consent, authenticated administration, role separation, rate limiting, and auditable sessions. Production deployments should additionally use HTTPS, unique high-entropy secrets, an isolated network, and a reviewed client rollout process.

## Releases and Packages

GitHub remains the public endpoint for release-facing metadata and future packages.

## License

MIT License. See [LICENSE](LICENSE).
