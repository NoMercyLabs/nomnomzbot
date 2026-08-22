# Product alignment — the goal every doc and spec realigns to

Owner decisions, 2026-08-22. This file wins over any older sentence in `.claude/docs/**`,
`CLAUDE.md`, `README.md`, `DEPLOY.md`. When a spec disagrees with this file, the spec is wrong.

## The product in one paragraph

NomNomzBot is an open-source, multi-tenant bot plus ONE uniform management dashboard for
streamers who go live on **Twitch, Kick, YouTube and X (Live) — at the same time**. A streamer has
**one channel with many platform connections**; commands, timers, event responses, settings,
moderation, economy, song requests, TTS and analytics are managed once and fan out to every live
platform; chat, mod queues and alerts fan in to one feed. A viewer is **one human across platforms**
(one identity, one balance, one standing, one ban). Three personas get working surfaces, in this
priority: **streamer → moderator of many channels → viewer**. Self-host is free and needs zero
NoMercy infrastructure; SaaS is restricted to NoMercy Labs and gives viewers a **free account**.
The bot **types** in chat via each platform's API (Twitch = Helix + EventSub; IRC is retired) and
**speaks** via TTS. We **stabilize the existing feature set before adding features**.

## Decisions (binding)

| # | Decision | Replaces |
|---|---|---|
| D1 | **One channel, many platforms.** `Channel` = the streamer's channel (tenant); each platform is a `PlatformConnection` under it. Config domains are shared with per-platform targets; per-viewer domains resolve to one human. | `platform-identity.md` §9.4 ("no channel-group; two Channel rows = two tenants"), `identity-auth.md` unique `OwnerUserId`, every "one Channel per platform" sentence. |
| D2 | **Any platform can be the first login.** Provider-generic sign-in (device code where the platform has it, code flow otherwise); the first linked platform creates the channel, others attach. Kick/YouTube/X login are shipped scope, not flags. | "Twitch remains the only shipped login provider", "Twitch-first", "login is Twitch-welded". |
| D3 | **X is a sibling streaming platform** (X Live + its chat). Posting tweets/clips is a later announcement-target feature, not now. | `platform-identity.md` §10 "X login-only". |
| D4 | **Free viewer account on SaaS.** Viewers sign in free to manage their own data (balance, TTS voice, pronouns, linked platforms, standing, GDPR) across every streamer on the instance; streamers pay. | "no viewer surface", MyData behind Moderator floor. |
| D5 | **Bot lines in chat carry a user-defined prefix** (none / `*` / `#` / emoji) so viewers can tell bot typing from the streamer typing when the bot uses the streamer's account. | Legacy fixed `"* "`; no marker. |
| D6 | **Vocabulary:** the bot **types** (chat) and **speaks** (TTS) — never "speaks" for chat. | Mixed usage. |
| D7 | **Track split suspended for the remediation campaign.** Claude works backend + frontend; `handoff/*.md` are not used; the design bar is the Sleak skill + shadcn catalogue (the designer's own rules). | `CLAUDE.md` Team & Track Ownership + Handoff TODOs as a hard gate. |
| D8 | **Stabilize before adding.** New feature ideas go to the tracker as ideas; code follows `SHORTCOMINGS-EXECUTION-PLAN.md` top to bottom. | Roadmap items framed as next features. |
| D9 | **No deferred framing.** Specs state decisions; "later / phase 2 / future / TBD" is replaced by a decided slice in the execution plan or deleted. | Scattered "later". |

## Glossary (canonical words — use these, retire the aliases)

| Canonical | Meaning | Retired aliases |
|---|---|---|
| **channel** (= tenant, `BroadcasterId`) | the streamer's one channel spanning platforms | "tenant" in prose, "broadcaster" for the row |
| **platform connection** | one platform's presence under a channel (Twitch/Kick/YouTube/X) | "platform channel", "sibling channel", "presence" |
| **broadcaster** | the human role (owner of the channel) | — |
| **viewer** (= `User`) | one human; `UserIdentity(Provider, ProviderUserId)` links platforms | "chatter", "participant" (UI rung name only) |
| **event response** | "when X happens do Y" config (incl. on-air alerts) | "reaction chain", "alert config", "automatic reaction" |
| **alert** | the on-air viewer-facing notification produced by an event response | "health alert", "ops alert" → **health signal** / **ops notification** |
| **widget** | the artifact (per-widget standalone Vue SPA) | "overlay" for the artifact, "browser source" as a noun |
| **overlay** | the OBS browser-source page rendering widgets | "host page" |
| **system surface** | a channel-owned page not installed from the gallery (TTS, sound) | "system widget" |
| **ManagementRole / CommunityStanding** | the two role ladders; users see NAMES, never level numbers | numeric levels in DTOs/tables |
| **Gate-1 / Gate-2 / Plane-C** | tenant admission / per-action keys / platform IAM — spelled exactly so | "Gate 2", "Plane C", "plane c" |
| **types / speaks** | chat output / TTS output | — |

## Domain table (dev/prod)

| Purpose | Domain |
|---|---|
| Deployed dev (Proxmox) dashboard + API | `https://dev.nomnomz.bot` (LAN `http://192.168.2.60:5080`) |
| Local-dev tunnel (owner's machine, OAuth redirects) | `https://bot-dev-api.nomercy.tv` — dev convenience only; self-host needs no NoMercy domain |
| Planned production | `https://api.nomnomz.bot` |
