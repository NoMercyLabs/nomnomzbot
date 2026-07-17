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

## Personal live-notification DMs (decided 2026-07-17)
- **A notify role can additionally deliver by direct message** — `DiscordNotificationRole.DmEnabled` (bool, default false, operator-controlled per role). Channel announce and DMs are independent outputs of the same dispatch: a config whose `PingRoleId` role has `DmEnabled` fans out DMs to that role's opted-in members after the channel post.
- **Recipients** = the role's `DiscordMemberOptIn` rows with `OptedOutAt == null`. Opt-in stays exactly the existing three-way design (manual role / button / API); opting out stops DMs immediately.
- **Delivery:** the dispatcher opens (or reuses) the member's DM channel — `POST /users/@me/channels` with `recipient_id` — caching the returned channel id on `DiscordMemberOptIn.DmChannelId` (string, null until first DM) so repeat go-lives skip the open call. The DM body is the same rendered template + embed as the channel post.
- **Idempotence + audit:** each DM is its own append-only `DiscordNotificationDispatch` row with `DedupeKey = "{baseDedupeKey}:dm:{discordMemberId}"` — the existing unique `(NotificationConfigId, DedupeKey)` index makes re-dispatch a no-op per member, and the dispatch log shows every DM.
- **Failure posture:** per-member best-effort, sequential fan-out — a member with DMs closed (Discord 50007) is logged and skipped, never fails the dispatch or the other members.
- **Chat-command opt-in (`!pingdiscord`) — decided: identity-link-gated.** It resolves the chatter's linked Discord identity (`UserIdentity`, provider=`discord`) and calls the existing opt-in; a viewer without a linked Discord identity gets a reply pointing at the link flow. It ships as its own slice after this one — this section records the decision so the fork is closed.
