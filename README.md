# NomNomzBot

Twitch bot management platform. Stream commands, channel point rewards, moderation, music, and more.

🌐 **Hosted:** [nomnomz.bot](https://nomnomz.bot)
📦 **Self-hosted:** Clone and run the setup script

## Quick Start

```bash
git clone --recursive git@github.com:NoMercyLabs/nomnomzbot.git
cd nomnomzbot
node setup.mjs
```

The setup script checks prerequisites, walks you through Twitch app creation, generates security keys, and starts everything.

## Structure

- `nomnomzbot-server/` — .NET 10 backend API
- `nomnomzbot-app/` — Expo/React Native dashboard (web + mobile)
- `nomnomzbot-design/` — Design mockups and research

## Docs

- [Deployment Guide](DEPLOYMENT.md)
- [Security Architecture](SECURITY_ARCHITECTURE.md)
- [AI Helper Prompt](CLAUDE.md)

## License

AGPL-3.0
