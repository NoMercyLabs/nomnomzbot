# Useful Local Dev URLs

| URL | Description |
|-----|-------------|
| `http://localhost:5090` | Dashboard dev server (Wasm, proxies `/api` + `/hubs` to 5080) |
| `http://localhost:5080/scalar` | Interactive API docs (Scalar UI) — Development only, or `Api:ExposeDocs=true` |
| `http://localhost:5080/health` | Full health status (JSON) |
| `http://localhost:5080/health/live` | Liveness probe |
| `http://localhost:5080/health/ready` | Readiness probe |
| `http://localhost:5080/health/version` | Running build version |
| `http://localhost:8082` | Adminer — Postgres browser (Docker stack only; absent in the default SQLite dev runtime) |
