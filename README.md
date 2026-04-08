# NomNomzBot

Twitch bot management platform. Stream commands, channel point rewards, moderation, music, and more.

🌐 **Hosted:** [nomnomz.bot](https://nomnomz.bot)
📦 **Self-hosted:** see below

---

## Deploy to a server (production)

**Requires: Docker only.** No .NET SDK, no Node, no Yarn — everything builds inside containers.

```bash
# Linux/macOS
git clone https://github.com/NoMercyLabs/nomnomzbot.git
cd nomnomzbot
chmod +x deploy.sh
./deploy.sh
```

```powershell
# Windows
git clone https://github.com/NoMercyLabs/nomnomzbot.git
cd nomnomzbot
.\deploy.ps1
```

The script:
1. Installs Docker if missing (Linux/macOS only — Windows users get a download link)
2. Generates all security keys automatically (`openssl rand`)
3. Prompts for Twitch credentials, your API domain, and your dashboard URL
4. Runs `docker compose up -d --build`
5. Waits for the health check and prints your URLs

---

## Develop locally

**Requires: .NET 10 SDK, Node 22, Yarn, Docker (for Postgres + Redis)**

```bash
git clone https://github.com/NoMercyLabs/nomnomzbot.git
cd nomnomzbot
node setup.mjs
```

The setup script checks prerequisites, writes the needed config, installs frontend packages if needed, and starts the dev environment.

---

## Structure

- `server/` — .NET 10 backend API
- `app/` — Expo/React Native dashboard (web + mobile)

## Docs

- [Deployment Guide](DEPLOYMENT.md)
- [Security Architecture](SECURITY_ARCHITECTURE.md)

## License

AGPL-3.0
