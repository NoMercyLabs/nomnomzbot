# NomNomzBot Server

.NET 10 backend API. Part of [NomNomzBot](https://github.com/NoMercyLabs/nomnomzbot).

To run the whole bot, use the deploy script at the repo root (full guide: [DEPLOY.md](../DEPLOY.md)):

- **Linux / macOS:** `./deploy.sh <desktop|docker|saas>`
- **Windows (PowerShell):** `.\deploy.ps1 <desktop|docker|saas>`

`saas` is a **restricted option** — hosting NomNomzBot as a service for others is against the
project license (reserved to NoMercy Labs); see [DEPLOY.md](../DEPLOY.md).

## Manual development

The commands below are identical on every OS. `dotnet run` alone starts the bot in **self_host_lite**
mode (SQLite, no infrastructure needed). To develop against the full profile (PostgreSQL + Redis),
bring the infrastructure up first — the boot probe detects it and switches automatically:

```bash
docker compose up -d postgres redis   # optional — full profile only
dotnet build
dotnet run --project src/NomNomzBot.Api
```
