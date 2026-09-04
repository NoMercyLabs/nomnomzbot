---
name: devbox
description: Start, enter, share or rebuild the containerised NomNomzBot work environment (devbox/) — browser VS Code, local VS Code Dev Containers, or Remote-SSH. Use when asked to set up the dev environment, run the editor in Docker, give someone else access to the work environment, or add a tool to it.
---

# The devbox

One container carrying every tool the repo's gates need, reachable **three ways**, all attached
to the same container and the same caches. It is the **editor**, not the bot — the bot's image
is the repo-root `Dockerfile`, and changes under `devbox/` ship nothing.

## Start

```powershell
Copy-Item devbox/.env.example devbox/.env    # set DEVBOX_PASSWORD
docker compose -f devbox/docker-compose.yml up -d --build
```

First build ≈ 10 minutes, ≈ 3.5 GB. Caches (NuGet, Gradle, extensions, `.vscode-server`) live on
named volumes, so a rebuild never re-downloads them.

## Three ways in

| Way | How |
|---|---|
| Browser | `http://localhost:8443`, password `DEVBOX_PASSWORD`. Nothing to install — this is the one to share. |
| Local VS Code | **Dev Containers: Reopen in Container** (`.devcontainer/devcontainer.json` targets this same compose service). |
| Remote-SSH | Put a public key in `devbox/ssh/authorized_keys`, restart. `sshd` runs key-only on **2222** as the unprivileged `dev` user. `DEVBOX_ENABLE_SSH=0` disables it. |

## Inside the box

The **host** docker socket is mounted, so containers you start are siblings, not nested:

```bash
docker compose up -d postgres redis adminer    # from /workspace
```

The devbox publishes **5080** and **5090**, so the API and the dashboard dev server are reachable
on the host exactly as they are without the container. PowerShell 7 is present, so every
`scripts/*.ps1` gate runs unchanged.

## Sharing it

`devbox/Caddyfile.hosted.example` puts it behind TLS + basic auth on a shared host. **The devbox
holds the docker socket — exposing it exposes the host.** Two locks, both required: Caddy basic
auth *and* `DEVBOX_PASSWORD`. Do not expose it without both, and confirm with the owner first.

## Changing it

- A tool everyone needs → add it to `devbox/Dockerfile`, rebuild, verify it answers `--version`.
- An extension → add its **Open VSX** id to `devbox/settings/extensions.txt` (installed on first
  start only; a missing one is skipped, never fatal). Do not install by hand — the next person
  will not get it.
- An editor setting → `devbox/settings/settings.json`. It is copied **only when the editor has no
  settings file yet**, so in-editor tweaks stick — and so editing it does **nothing to a box that
  already ran**. On an existing box, apply the same change inside it as the `dev` user:

  ```bash
  docker exec -u dev nomnomzbot-devbox \
    sh -c 'cat >> /home/dev/.local/share/code-server/User/settings.json'   # or edit it in the editor
  ```

  Never `docker cp` into it — that writes the file owned by root and every later save in the editor
  fails with `EACCES`. If you already did, `docker exec -u 0 … chown dev:dev` the file.

  Settings that are true **because it is a container** are not personal preference and must stay:
  `security.workspace.trust.enabled: false` (without it the workspace opens in Restricted Mode and
  every language extension stays dormant — no IntelliSense, no formatter, and it reads like a failed
  extension install), the Linux terminal profile, and `files.eol: "\n"`.
- Never put a secret in the image. The devbox reads `.env` the same way the API does.

## Report back

Whether it built, the container health, and the version each tool reported. A tool that does not
answer `--version` is not installed, whatever the build log said.

**Then open `http://localhost:8443` and look at the rendered editor** — `docker exec` proves the
toolchain and says nothing about the editor someone will actually sit in, where every defect is
silent. Confirm on the page, not in a log:

- **No Restricted Mode banner** across the top. If it is there, the language extensions are
  dormant — fix the trust setting, do not dismiss the banner.
- The theme and file icons actually rendered, not the defaults — proof their extensions resolved
  on Open VSX.
- The terminal opens a shell, and the status bar shows no activation errors.
- Open a `.cs` and a `.kt` and confirm IntelliSense answers.
