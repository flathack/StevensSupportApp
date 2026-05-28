# StevensSupportApp

Public distribution facade for Stevens Support Helper.

This GitHub repository is kept for public packages, release metadata, and deployment examples. The application source code is maintained in a private repository and is not published here.

## Container Image

The public container image is published to GitHub Container Registry:

```text
ghcr.io/flathack/stevens-support-app:latest
```

Use the included `docker-compose.ghcr.yml` as a starting point for deployments.

## Quick Start

```powershell
copy .env.example .env
docker compose -f docker-compose.ghcr.yml --env-file .env up -d
```

The default compose file exposes the web service on port `5000`.

## Releases and Packages

GitHub remains the public endpoint for packages and release-facing metadata. Source code, build context, and private deployment material are intentionally not part of this public repository.

## License

MIT License. See [LICENSE](LICENSE).
