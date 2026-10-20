# Install NomNomzBot with Docker, without Postgres or Redis

**Audience:** streamers who want to run their own bot in a container.
**Owner:** NoMercy Labs.
**Date:** 2026-10-20.

## Contents

- [The goal](#the-goal)
- [Lite or full: which one](#lite-or-full-which-one)
- [Install](#install)
- [Where your data lives](#where-your-data-lives)
- [Back up your data](#back-up-your-data)
- [Update the bot](#update-the-bot)
- [Limits](#limits)
- [Glossary](#glossary)

## The goal

You run the bot in one container. You need no database server. You need no cache server.
The bot keeps everything in one folder. You can copy that folder to back it up.

## Lite or full: which one

Pick **lite** when you run the bot for your own channel. Lite is the simplest path.

Pick **full** when you want a database server, or when you want updates with no pause. Full runs Postgres and Redis next to the bot.
See [the deploy guide](../DEPLOY.md) for full.

| | Lite | Full |
|---|---|---|
| Containers | 1 | 4 or more |
| Database | SQLite file | Postgres |
| Cache | In the bot | Redis |
| Updates with no pause | No | Yes |

## Install

You need Docker with Compose v2. You also need a Twitch application for the Client ID.

1. Download the project files, or clone the repository.
2. Open a terminal in the project folder.
3. Run this command:

```bash
docker compose -f docker-compose.lite.yml up -d --build
```

4. Open `http://localhost:5080` in your browser.
5. Follow the setup wizard. You can enter your Twitch credentials there.

You can also put settings in a `.env` file first. Copy `.env.example` to `.env`. All values are optional in lite.
If you leave the secrets empty, the bot makes its own and saves them in the data folder.

To use a ready-made image, set `API_IMAGE=ghcr.io/nomercylabs/nomnomzbot:latest` in `.env`. Then leave out `--build`.

To use another port, set `API_HTTP_PORT` in `.env`.

## Where your data lives

The bot keeps all its data in `/app/data` inside the container. This includes the database, the secrets, your uploads and the logs.

By default Docker stores this in a named volume called `nomnomzbot_lite_data`.

You may prefer a plain folder that you can see and copy. Open `docker-compose.lite.yml`. Remove the `#` from the line `./data:/app/data`. Remove the line with `lite_data:/app/data`.
Then make the folder before you start the bot:

```bash
mkdir data
sudo chown 1654:1654 data
```

Keep the whole folder together. The database has side files that end in `-wal` and `-shm`. Never copy the database file alone.

## Back up your data

1. Stop the bot: `docker compose -f docker-compose.lite.yml stop`
2. Copy the data folder. With the default volume, run this command:

```bash
docker run --rm -v nomnomzbot_lite_data:/data -v "$PWD":/backup alpine tar czf /backup/nomnomzbot-backup.tgz -C /data .
```

With a bind mount, copy the `data` folder instead.

3. Start the bot: `docker compose -f docker-compose.lite.yml start`

To restore, stop the bot. Put the files back in the data folder. Start the bot.

## Update the bot

Pull the new image, or build again. Then run the same `up -d` command. Your data stays in the volume.

## Limits

- Run one container only. SQLite allows one writer. Do not run two copies on the same data.
- There is no zero-downtime update. The bot pauses for a few seconds on each update.
- Do not put the data folder on a network drive. SQLite needs a local disk.

## Glossary

| Term | Meaning |
|---|---|
| SQLite | A database that lives in one file. It needs no server. |
| Postgres | A database server. Full mode uses it. |
| Redis | A cache server. Full mode uses it. |
| Docker | A tool that runs programs in containers. |
| Compose | A Docker tool that starts containers from one file. |
| Volume | Storage that Docker keeps outside the container. |
| Bind mount | A folder on your computer that the container uses directly. |
| WAL | A side file SQLite uses for safe writes. |
