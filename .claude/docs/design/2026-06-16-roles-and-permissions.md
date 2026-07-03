# Roles & Permissions — Model (DRAFT)

Source: design dialogue 2026-06-16. Status: **DRAFT for review.** Feeds the permissions epic + Gate 2 (per-action authorization). Pairs with the tenant-resolution / Gate 1 fix already shipped.

## Fundamental: opt-in / default-deny

**Every "allow someone to do X" is opt-in / default-deny.** Out of the box the bot applies **Twitch's own role rules** (a mod can do mod things, a VIP what VIPs do, etc.); *anything beyond Twitch's defaults requires explicit broadcaster opt-in.* This is the spine under everything below — the floors, per-action levels, `!permit`, and message-element gating all express it.

## Two axes — Twitch roles are not one ladder

They only overlap at **Moderator**:

- **Community standing** (chat badges; earned/granted): `Viewer → Subscriber → VIP → Artist → Moderator`. Gates **chat-command** usage + cosmetics. Sourced from chat tags / EventSub badges.
- **Channel management** (administer the channel): `Moderator → Editor → Broadcaster`. Gates the **dashboard / HTTP API** (Gate 2). Editor sourced from Helix `channels/editors`, not chat badges.

A Subscriber/VIP has community standing but **zero** management power — they never appear on the management ladder.

## Role reality-checks (vs the user's list)

- **Super Mod** — NOT a real Twitch role. Custom bot tier (a broadcaster-elevated mod). We define its powers.
- **Editor** — real Twitch role; for *management* it ranks **above** moderator. Fetched via Helix `channels/editors` (needs a sync/store), not chat badges.
- **Artist** — real but **cosmetic** (community honor ≈ VIP); no powers. Community axis only.
- **Staff** — Twitch **global employees**; not channel-grantable. Display badge only; excluded from authz.
- **Platform Admin** — our own role (`User.IsAdmin`), above broadcaster for cross-channel ops.

## Proposed model — single ordered `PermissionLevel`, used as a minimum-level gate

**Authz ladder (Gate 1 entry + Gate 2 per-action):**

| Level | Value | Example minimum-level actions |
|---|---|---|
| Moderator | 10 | enter dashboard, timeout/ban, delete message |
| SuperMod (custom) | 20 | manage other mods, bot config |
| Editor | 30 | edit stream title/game/tags, run ads |
| Broadcaster | 40 | integrations/tokens, billing, danger-zone, channel delete |

> Platform / cross-tenant access is **not** a rung on this ladder — it lives in **Plane C — Platform IAM** below. (The current `User.IsAdmin` bypass in `ChannelAccessService` is a crude interim for it.)

**Community ladder (chat commands + cosmetics):** `Everyone < Subscriber < VIP < Moderator < Broadcaster` — formalize the levels `ChatMessageHandler.HasPermission` already uses.

## Decisions / simplifications

- Mod vs Editor don't perfectly nest in real Twitch (different powers). We **decide** a linear trust order (`mod < editor`) for simplicity; add per-capability overrides only on real need (Rule of Three) — **no full capability matrix on day one.**
- Gate 1 (`ChannelAccessService`) currently allows **broadcaster + moderator + platform-admin**. **Editor + super-mod** entry land with this epic (editor needs a Helix editors sync + store).
- Each HTTP endpoint/action declares its required min level; the resolved caller's level for the channel must be ≥ it. This replaces the current "almost everything is just `[Authorize]`" gap.

## Per-action levels are configurable (with floors)

Each action ships a **default** min level, but the broadcaster can **override** it per channel (raise or lower). This is the permissions screen: a list of actions, each with a level dropdown.

The gate is **one ordered ladder** — `Everyone < Subscriber < VIP < Moderator < SuperMod < Editor < Broadcaster` — and each action has a **default level** plus a **floor** below which the broadcaster cannot set it.

Resolution: `effectiveLevel(action, channel) = clamp(channelOverride[action] ?? default[action], floor[action], Broadcaster)`.

**Principle: protect where there is real danger, don't lock what's harmless.** The floor is set by *danger to the channel* (account / financial / security loss) and *TOS-violation risk* (public content that could get the channel struck) — not a flat two-tier:

- **Critical → floor = Broadcaster** (never lowerable): integrations / OAuth **tokens**, billing, channel delete, managing mods/roles. Account, money, or security harm.
- **TOS / reputation → floor = Moderator** (lowerable to Mod, never to VIP/viewer): stream **title**, game, tags, shoutouts — anything published publicly *as the channel*. A viewer or VIP could put a slur in the title and get the **streamer's** channel suspended, so it floors at a vetted role (Mod).
- **Low danger → floor = VIP or Everyone** (broadcaster's call): polls, song requests, fun commands. Open to VIPs — or everyone — if the owner wants.

Defaults ship **higher** than the floor (sensible out-of-box, e.g. title defaults to Editor); the broadcaster tunes down to the floor, never below. Floor assignment is a per-action danger/TOS review.

Overrides are stored per channel; absent an override, the default applies.

## Individual grants (`!permit`) — per-user, on top of Twitch roles

The broadcaster (or a sufficiently-privileged role) can grant a *specific user* elevated access **without** them holding the Twitch badge. e.g. `!permit @ifillz mod` (whole role) or `!permit @ifillz channel:title:write` (one capability). ifillz stays VIP on Twitch but is treated as Mod (or title-capable) inside the bot.

- **Effective access = MAX(Twitch-badge role, bot role grants, individual capability grants).** Grants are stored per `(channel, user)`, decoupled from Twitch.
- Whole-role grant **or** single-capability grant. Companion `!unpermit`. Optionally **time-boxed** (e.g. this stream only) with expiry.
- **Guardrail 1 — Critical tier is never grantable** via `!permit` (tokens, billing, channel delete, managing mods/roles): broadcaster / IAM only. TOS and Low tiers are grantable.
  - So a trusted VIP *can* be handed `title:write` individually even though the global title floor is Mod — an explicit per-person trust decision, not a tier-wide lowering.
- **Guardrail 2 — no escalation above self:** a grantor can never grant a level/capability higher than their own.
- Who may issue `!permit` is itself a permission (default Broadcaster; lowerable to Editor).

## Plane C — Platform IAM (SaaS only)

A **separate** system for NoMercy Labs operators + service accounts running the SaaS — not streamers. Built on IAM principles: **least-privilege, default-deny, audited.** Distinct from the two per-channel planes (A community, B channel-management).

- **Fine-grained permissions** on platform resources: e.g. `tenant:read`, `tenant:suspend`, `tenant:access` (cross-tenant data), `billing:read` / `billing:refund`, `featureflag:write`, `audit:read`, `iam:manage`.
- **Roles = named permission bundles:** Support-Agent, Billing-Admin, Read-Only-Auditor, On-Call-Engineer, etc. Operators get only what the job needs.
- **Principals:** platform employees **and** machine service accounts (least-privilege identities for automation).
- **Cross-tenant access is gated + audited.** A support agent reading a tenant's data uses `tenant:access`, granted narrowly and logged every time (who / what / when / why). Consider time-boxed / break-glass elevation for the most sensitive ops. This is how "GDPR on every surface" is enforced for our **own** staff, not only tenants.
- **Replaces `User.IsAdmin`** — a single boolean is too coarse for least-privilege.
- **Self-hosted: N/A.** One operator owns the box; Plane C collapses to "owner = full." SaaS-only, on the deployment-mode axis.
