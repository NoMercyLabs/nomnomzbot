# NomNomzBot

NomNomzBot is a free chat bot for live streamers.
You run it on your own computer or server, and you control it from a web dashboard.
One channel can connect Twitch, Kick, YouTube and X Live at the same time.

The code is public. You may use, change and share NomNomzBot for free, and run it for yourself, your friends and your community.
You may not earn money with it: no selling it, and no paid hosting or paid service built on it. That right is reserved to NoMercy Labs.

[![License: AGPL-3.0](https://img.shields.io/badge/license-AGPL--3.0-blue.svg)](server/LICENSE) [![Status: pre-release](https://img.shields.io/badge/status-pre--release-orange.svg)](#where-to-get-help)

The full reference is at **https://docs.nomnomz.bot**.

## Contents

- [What it can do](#what-it-can-do)
- [How to start](#how-to-start)
- [Where to get help](#where-to-get-help)
- [For developers](#for-developers)
- [License](#license)

## What it can do

### Chat and commands

- Make your own chat commands. A simple command is one line of text.
- Build bigger commands in a visual editor, with steps, conditions and branches.
- Put live values in any message, such as the viewer name or the stream uptime. There are more than 90.
- Turn on the built-in commands you want, and change their replies.
- Set timers that post a message on a schedule.
- Set chat triggers that answer a keyword.
- Keep a quote book.
- Chat from the dashboard, with 7TV, BTTV and FFZ emotes shown.
- Import commands, quotes and timers from StreamElements.

### Viewers and points

- See your real viewers, their activity and their watch streaks.
- Give your channel its own currency, with earning rules, a store and leaderboards.
- Run chat games and overlay games.
- Gambling games are optional. Each viewer must confirm they are 18 or older.
- Run giveaways, and draw winners.

### Rewards

- Create, edit and delete channel-point rewards.
- Fulfill or refund redemptions from a queue.
- Let a redemption start a command flow.

### Music

- Control Spotify or YouTube playback from the dashboard.
- Take song requests from chat, with a fair queue.
- Give viewers a public request page. They need no account.
- Block tracks you do not want.

### Voice

- Read chat aloud with text-to-speech (Azure or ElevenLabs).
- Let viewers pick their own voice with `!voice`.
- Approve messages before they are read, and filter bad words.
- Play sound clips from commands.

### Overlays and widgets

- Add overlays to OBS. Paste the overlay link into an OBS browser source.
- Show alerts for follows, subs, cheers, raids and gifts.
- Install widgets from the gallery, change their settings, or write your own.
- Control OBS scenes and VTube Studio from the bot.

### Moderation

- Time out, ban and unban from one page.
- Set AutoMod levels, blocked terms and chat filters.
- Review unban requests, reports and the AutoMod queue.
- Keep notes and a history for each viewer.
- Use layered spam defense. It starts in test mode, so it only watches until you turn it on.

For how the bot keeps your community safe, read [Community safety](docs/community-safety.md).

### Integrations

- **Discord:** post a message when you go live, and give live roles.
- **Spotify** and **YouTube** for music.
- Ko-fi, GitHub, Fourthwall, Shopify, Patreon and Buy Me a Coffee can trigger the bot.
- Other apps can send events to the bot, and get events from it (webhooks).
- Your own tools can control the bot with a key you make and can cancel (automation API).

Every feature is listed in [FEATURES.md](FEATURES.md).

## How to start

This runs the bot on your own machine with Docker.
Self-hosting your own bot is always free.

### What you need

- [Docker](https://docs.docker.com/get-docker/), the free app that runs the bot in a container.
- A Twitch application. Make one in the [Twitch Developer Console](https://dev.twitch.tv/console/apps).
  - Set its OAuth Redirect URL to `http://localhost:5080/api/v1/auth/twitch/callback`.
  - Copy its Client ID and Client Secret. You need them in step 3 or in the setup wizard.

### Steps

1. Download or clone this repository.
2. Open a terminal in the project folder.
3. Run the deploy script.

   Linux or macOS:

   ```bash
   ./deploy.sh docker
   ```

   Windows (PowerShell):

   ```powershell
   .\deploy.ps1 docker
   ```

   The script makes a `.env` file with new secret keys.
   It asks for your Twitch Client ID and Secret. Press Enter to skip, and enter them in the dashboard later.
   It then starts the bot and waits until it is ready.

4. Open `http://localhost:5080` in your browser.
5. Follow the setup wizard. It connects your accounts and asks for each value it needs.

### Other ways to run it

- **One container, no Postgres or Redis:** read [Install with Docker, without Postgres or Redis](docs/install-docker-lite.md).
- **One file, no Docker:** run `./deploy.sh desktop`. Read [DEPLOY.md](DEPLOY.md).
- **Public access with HTTPS:** read [DEPLOY.md](DEPLOY.md).
- **SaaS mode:** for NoMercy Labs only. You may not earn money with NomNomzBot: no selling it, and no paid hosting or paid service built on it. That right is reserved to NoMercy Labs. Read [DEPLOY.md](DEPLOY.md).

## Where to get help

NomNomzBot is pre-release software. Expect changes.

- Ask a question or report a bug in [GitHub issues](https://github.com/NoMercyLabs/nomnomzbot/issues/new/choose).
- Report a security problem in private. Read [SECURITY.md](SECURITY.md).

## For developers

- How to contribute, and the checks a pull request must pass: [CONTRIBUTING.md](CONTRIBUTING.md).
- All deployment options and settings: [DEPLOY.md](DEPLOY.md) and [.env.example](.env.example).
- The backend is in `server/` (.NET). The dashboard is in `dashboard/` (Vue).
- The running bot shows its API docs at `http://localhost:5080/scalar`.

## License

The code is public. You may use, change and share NomNomzBot for free, and run it for yourself, your friends and your community.
You may not earn money with it: no selling it, and no paid hosting or paid service built on it. That right is reserved to NoMercy Labs.

The legal text is [AGPL-3.0-or-later](server/LICENSE). Copyright © NoMercy Labs.
The names and logos are covered in [TRADEMARKS.md](TRADEMARKS.md).
