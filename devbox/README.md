<!--
  Copyright (c) NoMercy Labs.
  SPDX-License-Identifier: AGPL-3.0-or-later
-->

# Devbox — the containerised work environment

One container carrying every tool this repo's gates need, with **three ways in**: a browser
editor, your local VS Code, or VS Code from another machine over SSH. All three attach to the
same container, the same caches, the same running processes.

This is the **editor**, not the bot. The bot's own image is the repo-root `Dockerfile`; changes
here ship nothing.

## Start it

```bash
cp devbox/.env.example devbox/.env      # set DEVBOX_PASSWORD
docker compose -f devbox/docker-compose.yml up -d --build
```

First build pulls the .NET SDK, a JDK, Node and code-server — expect ~10 minutes and ~4.7 GB.
Rebuilds are cached, and every cache that matters (NuGet, Gradle, extensions) lives on a named
volume, so a rebuild never re-downloads them.

## The three ways in

### 1. Browser — code-server

<http://localhost:8443>, password from `DEVBOX_PASSWORD`. Nothing to install; this is the one
to share.

### 2. Local VS Code — Dev Containers

Open the repo, then **Dev Containers: Reopen in Container** (`.devcontainer/devcontainer.json`).
It attaches to this same compose service — the browser editor keeps running alongside.

### 3. Another machine — Remote-SSH

Put your public key in `devbox/ssh/authorized_keys` (one per line) and restart the container.
`sshd` starts key-only on port 2222, running **as the unprivileged `dev` user**, so the only
login it can grant is that user. Then in VS Code, **Remote-SSH: Connect to Host**:

```
Host nomnomzbot-devbox
    HostName <the docker host>
    Port 2222
    User dev
```

Leave `devbox/ssh/authorized_keys` empty (or set `DEVBOX_ENABLE_SSH=0`) and no SSH server runs
at all.

## Identity, credentials and memory

Three things a fresh container does not have, and each fails in a way that does not name itself:

- **Git identity.** `~/.gitconfig` is mounted read-only, so commits made in the box are attributed
  to you rather than `dev@devbox`. Override the path with `DEVBOX_GITCONFIG` (Windows hosts need
  a full path — `~` does not expand as you expect).
- **GitHub auth.** Run `gh auth login` inside the box. Credentials are created there, never
  forwarded in from the host, so each one is scoped to the box and revocable on its own.
- **Memory.** A container sees the *host's* total RAM, so the Gradle and Kotlin daemons size their
  heaps for a machine they do not have and get OOM-killed mid-build — which surfaces as a compiler
  crash, not as an out-of-memory error. `GRADLE_OPTS` caps them and `mem_limit` (default 8g,
  `DEVBOX_MEM_LIMIT`) caps the box, so a runaway build dies instead of the host.

Claude Code is installed; log in inside the box on first use.

## Bind-mount performance

On Docker Desktop (Windows/macOS) the bind-mounted repo crosses a VM boundary and is markedly
slower than native disk. That is why every dependency cache — NuGet, Gradle, npm, extensions —
lives on a **named volume** instead of under the repo: those are the paths a build hammers. If a
build is still painfully slow, the fix is to move its output directory onto a volume too, not to
add more RAM.

## What's inside

| Tool | Why |
|------|-----|
| .NET 10 SDK | `server/` — build, test, `dotnet-ef` |
| csharpier 1.3.0 · jb (ReSharper CLI) 2026.2.1 · dotnet-ef | restored on start from `server/.config/dotnet-tools.json` — the exact pins `scripts/slice-check.ps1` needs |
| JDK 21 | `app/` — Gradle wrapper, KMP + Compose Multiplatform |
| Node 22 + npm | widget / Vue SFC tooling |
| PowerShell 7 | `scripts/*.ps1` run unchanged |
| git · git-lfs · gh | the CI gate (`gh run watch`) |
| docker CLI + compose plugin | drives the **host** daemon through the mounted socket (`sudo` on Docker Desktop) |
| Claude Code CLI | log in inside the box on first use |
| cloudflared | HTTPS tunnel for Twitch OAuth redirects in local dev |

## Running the stack from inside

The docker socket is mounted, so containers you start are **siblings on the host**, not nested.
Docker Desktop hands the socket over root-owned, so prefix with `sudo` there (the `dev` user has
passwordless sudo); on a Linux host the entrypoint joins the socket's group and plain `docker`
works:

```bash
sudo docker compose up -d postgres redis adminer   # from /workspace
cd server/src/NomNomzBot.Api && dotnet run      # API on 5080, published to the host
cd app && ./gradlew :composeApp:wasmJsBrowserDevelopmentRun --watch-fs -t   # 5090
```

Both ports are published by the devbox itself, so `http://localhost:5080` and `:5090` work on
the host exactly as they do without the container.

## Running the gates

`scripts/slice-check.ps1` and the other `scripts/*.ps1` run unchanged. Build output goes to
`/home/dev/artifacts` (`ArtifactsPath`), off the bind mount — sharing `obj/` with a host build
fails with MSB3374 because those files are uid 0 and Docker Desktop refuses the timestamp update.

Measured in the box: full solution build 3m20s, Domain suite 110ms, csharpier instant. The
`jb inspectcode` leg takes **20+ minutes** — ReSharper reads the whole solution through the
Docker Desktop VM share regardless of `--include`. Run that leg on the host.

## Sharing it

`Caddyfile.hosted.example` is the config for putting the devbox behind TLS + basic auth on a
shared host. Read the warning at the top of it first: **the devbox holds the docker socket, so
exposing it exposes the host.** Two locks, both required — Caddy basic auth and
`DEVBOX_PASSWORD`.

## Editor settings and extensions

`settings/settings.json` and `settings/extensions.txt` are committed, so every host gets the
same editor. The extension list is re-applied whenever `extensions.txt` changes, and an id Open
VSX does not carry is skipped, never fatal. Removing an id does not uninstall it — use
`code-server --uninstall-extension <id>` for that. Add an extension to the list rather than
installing it by hand, so the next person gets it too. Settings are **copied** on first start, so
tweaking them inside the editor sticks.
