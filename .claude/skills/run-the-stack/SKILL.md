---
name: run-the-stack
description: Start, verify and stop the NomNomzBot stack locally — API, dashboard dev server, Postgres/Redis, and the authenticated browser check. Use when asked to run the app, launch the API, reproduce something live, refresh the OpenAPI snapshot, or prove a feature actually works in the rendered client.
---

# Run the stack

## The API

```powershell
scripts/dev-api.ps1 start    # dotnet run --no-build in the background, waits for /health
scripts/dev-api.ps1 status
scripts/dev-api.ps1 stop     # stops whatever owns the port
```

Use the script, not a bare `dotnet run` — the by-hand
"run → `Get-NetTCPConnection` → `Stop-Process`" sequence drifts, and a leftover process holds
the build DLLs and turns the next build into fake compile errors. Default port **5080**, always;
that is the committed default and the registered OAuth redirect origin.

`dotnet run` in Development with no Postgres/Redis reachable resolves to **SelfHostLite on
SQLite** — the appsettings Postgres connection string is unused in that mode. Do not diagnose a
data problem against Postgres when the process is on SQLite.

Optional backing services (full profile only):

```powershell
docker compose up -d postgres redis adminer
```

## The dashboard

```powershell
& app\gradlew.bat -p app :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t
```

Serves on **5090** and proxies `/api` + `/hubs` to `http://localhost:5080`. No flags, no env
vars, on a fresh clone. Both together: `start.sh`.

## Verifying — the part that gets skipped

**A 200 from the API is not proof the feature works.** The client can still fail to render: a
404 on an unknown file type, a runtime exception, a window that opened behind another. Before
saying anything works:

1. `http://localhost:5080/health` — the API is actually up.
2. `http://localhost:5080/health/version` — returns `{"version":"0.1.0+<full 40-char sha>"}`; confirm the
   `+<sha>` suffix is the **commit** being served, when it matters. A local `dotnet run` build carries
   no stamped SHA, so this check is meaningful for the Docker image only.
3. Load the real client and check **every fetched resource** returns 200, not just the page.
4. Drive the actual control. Then **reload** and confirm the write survived.

For an authenticated check: mint a token with `python3 scripts/mint-jwt.py --secret <Jwt__Secret> --sub <userId guid>`
(add `--tenant <broadcasterId guid>` and `--roles user,admin` as the endpoint needs) and open the app with
`#access_token=<jwt>`. The secret is used as raw UTF-8 bytes, not base64-decoded. A self-host instance does
not sign with the appsettings placeholder: read the secret from `<data-dir>/keys/jwt-secret.bin`
(plaintext on Linux, DPAPI-sealed on Windows). A wrong secret shows as 401 "The signature key was not found".

An overlay- or preview-only output (TTS, alerts, widget previews) needs a **presence check** —
"it spoke" with nothing attached to hear it is a false success.

## Refreshing the OpenAPI snapshot

Run `scripts/refresh-openapi.ps1`, then `:composeApp:jvmTest` so `ApiContractTest` AND
`ApiRouteContractTest` pass. **Never commit a consumer without its contract.**

## Report back

What you started, what you drove, what you observed — the rendered result, not the HTTP status.
If a resource 404'd or a control did nothing, say exactly which one.
