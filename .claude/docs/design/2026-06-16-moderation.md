# Moderation Tooling — Design (DRAFT)

Source: design dialogue 2026-06-16 (decisions via Q&A). The "tools *for* moderators" differentiator.

## Mod surface
- **Dedicated mod dashboard** — a mod's-eye view distinct from the streamer dashboard: action queue, quick actions, per-user context. Gated ≥ Moderator; what's visible/doable scales with level.

## Core tools
- **Unified action queue:** AutoMod-held messages + viewer reports + bot flags in one place; approve / deny / timeout / ban inline.
- **User context:** shared mod notes, action/warning history, trust/heat score (reuse `TrustScoreCalculator`).
- **Custom filters:** regex, blocklists, link policy — per channel, opt-in.
- **Mod-action audit log:** who did what, when, why — every action recorded.

## Cross-channel moderation
- **Default:** channel rules apply — no cross-channel ban.
- **Shared-chat ban propagation:** during an **active** Twitch Shared Chat session, a ban issued by a **super-mod on their own channel** propagates to a partner channel **iff**: (a) the partner broadcaster has **enabled "accept shared-chat bans,"** and (b) the partner channel is registered to a listening bot — **any deployment, self-host *or* SaaS**. Delivered cross-instance via the federation event-bus (ban is an opt-in shareable event). The actor needs super-mod on the **originating** channel only (not the partner). Risky → super-mod tier only. Audited + reversible on both sides.
- Shared ban/trust lists between trusted channels = opt-in via the federation trust model, same super-mod gating.

## "Network nuke" (cross-channel mass action) — split by safety
- ✅ **Ban-everywhere-I-mod:** ban a user across every channel the actor holds ban rights on, in one action. **Super-mod+ only**, one confirmation, **audited + reversible** (un-nuke). Legit — uses the real ban API on channels you control. Strong against serial harassers/bots.
- ⚠️ **NO automated mass-reporting to Twitch.** Coordinated/mass reports = **report brigading, against Twitch TOS** — could get the reporting channels (and the whole platform) banned for abuse. Instead: generate an **evidence packet** (offending messages, clips, timestamps, context) that makes it one-click to file a *legitimate individual* report through Twitch's own flow. Help streamers report correctly; never weaponize the report system.
