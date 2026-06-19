# NomNomzBot Server

.NET 10 backend API. Part of [NomNomzBot](https://github.com/NoMercyLabs/nomnomzbot).

Run `node setup.mjs` from the parent repo to get started.

## Manual Development

```bash
docker compose up -d postgres redis
dotnet build
dotnet run --project src/NomNomzBot.Api
```
