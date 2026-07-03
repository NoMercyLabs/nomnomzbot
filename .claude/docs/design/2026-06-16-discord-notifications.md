# Discord Notifications — Design (DRAFT)

Source: design dialogue 2026-06-16 (decisions via Q&A). Multi-tenant Discord go-live + event notifications.

## Triggers (all opt-in)
- **Go-live** (stream online) — the core. Plus **new clip / highlight**, **schedule / upcoming** reminders, **milestones** (follower/sub goals). Each opt-in per the fundamental.

## Multi-tenant, both-opt-in control
- Many streamers per Discord server (community/team servers). Each streamer = own notification config + role.
- **Both sides consent:** the **server admin** allows the streamer + configures (target Discord channel, ping role, message template); the **streamer** enables their channel for that server. Nothing fires unless both opted in — kills spam + abuse.

## Member opt-in (who gets pinged)
Members self-assign the per-streamer notify role, three ways:
1. **Manual Discord role** — grab it themselves.
2. **Command** — `!pingdiscord` (or similar) toggles the role.
3. **Bot button messages** — our Discord bot posts an interaction message with buttons; clicking toggles the role (cleanest UX).

Role-per-streamer so members pick exactly which streamers they want pings for.

## Mechanics
- Our Discord bot (per server) manages roles + posts notifications. Go-live is driven by the Twitch EventSub `stream.online` event → bot posts to the configured channel, pinging the role.
- **Dedupe:** one notification per go-live (no double-posting on stream flaps).
