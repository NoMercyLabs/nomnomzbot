---
name: deploy-and-verify
description: Deploy NomNomzBot to a host and prove the deployed box is running the intended commit — ship.ps1 (CI-gated pipeline) and switchover.ps1 (zero-downtime blue/green). Use when asked to deploy, ship, release, restart the server, or check what the box is actually running.
---

# Deploy and verify

**Deploy only when the owner calls it.** Validated ≠ deployed; green CI ≠ deployed. The deployed
SHA is the only ground truth.

Host config comes from env vars, never committed:

```
NOMNOMZ_DEPLOY_SSH   root@192.0.2.10
NOMNOMZ_DEPLOY_KEY   $HOME\.ssh\deploy_key
NOMNOMZ_DEPLOY_DIR   /opt/nomnomzbot   (default)
```

## The whole pipeline — ship.ps1

```powershell
.\scripts\ship.ps1                # HEAD of the current branch
.\scripts\ship.ps1 -Sha <sha>
```

Watches CI for that commit; on **green** pulls + restarts on the host, verifies health and image
freshness, and prints one compact report. **Red CI deploys nothing and exits 1.**

`ship.ps1` syncs `docker-compose.yml`, `Caddyfile` and `guard-single-color.sh` to the host, reloads
Caddy (after this plan's `ship.ps1` edit) and installs the single-colour guard cron. When the box
misbehaves afterwards, run `scripts/proxmox-triage.ps1` (read-only).

## Zero-downtime — switchover.ps1

```powershell
.\scripts\switchover.ps1                      # local compose stack
.\scripts\switchover.ps1 -ReadyTimeoutSec 180 # slow box / cold pull
.\scripts\switchover.ps1 -Build               # force a local rebuild
.\scripts\switchover.ps1 -DrainSec 35          # seconds the old colour gets to finish in-flight requests (default 35)
```

With `NOMNOMZ_DEPLOY_SSH` + `NOMNOMZ_DEPLOY_KEY` set, `switchover.ps1` acts on the REMOTE host; unset
them for the local stack.

Caddy fronts **two** API services and routes only to whichever passes `/health/ready`. The script
starts the **idle** colour, waits for it to become ready, and only then stops the old one.

- **There is no `api` service.** Deploy a *colour*: `api-blue` / `api-green`.
- Re-runnable: it reads `docker ps` to work out which colour is live, so a second run converges
  instead of double-switching.
- Failure contract: if the new colour never becomes ready, it is stopped, the **old colour keeps
  serving**, and the script exits non-zero. Nothing stops the old colour before the new one has
  proven itself.

## Verify — the step that makes it true

```bash
curl -fsS https://<host>/health/version    # the version field must end with +<the full 40-char SHA you intended>
curl -fsS https://<host>/health/ready
```

`/health/version` returns `{"version":"0.1.0+<sha>"}` and exists precisely to answer "which commit is this box running?". If it does not
match, the deploy did **not** land — say that plainly rather than reporting success. Then
re-verify a couple of features that were already working; a deploy that breaks something else is
a failed deploy.

## Traps

- A drifted `.env` on the host resurrects stale variables (a stale Twitch `client_secret` shadows
  the env one and produces 401s). Check the host's `.env` when auth breaks only in deployment.
- Rotating `ENCRYPTION_KEY` makes every stored OAuth token unreadable — the bot must re-auth.
- Two migration assemblies: a SQLite-only migration breaks Postgres deployments on upgrade. Run
  `scripts/migration-check.ps1` for **both** providers before shipping schema changes.

## Report back

Intended SHA, `/health/version` actual, ready check, which colour is now live, and the
regression re-check. If they disagree, lead with the disagreement.
