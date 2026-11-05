# Shared game platform — discovery notes (not settled)

Date: 2026-11-05. Captured by Claude from a live discovery chat with Stoney. **Status: exploratory —
nothing here is locked.** The chat was cut short by context size, not by a decision to stop; resume it
before treating anything below as final. Do not build from this without Stoney confirming the open
questions in §6 first.

## 1. Origin

Stoney, opening request (translated): "I'd like to facilitate collaboration with other bots — a shared
game state, the way we already do it internally, but where other bots can query our data and subscribe
to our EventSub, without having to fully onboard. I don't know where to start, how to approach this, or
exactly what I want. I'd like you to interview me about it."

This grew, over the conversation, from "let another bot read our data" into a much larger idea: a
general-purpose, persistent game/app platform inside NomNomzBot. The notes below are in the order the
shape actually emerged, because the order matters for why each piece is there.

## 2. What already exists (checked against code, not assumed)

- **Federation subsystem** (`FederationPeers` / `ChannelFederationOptIns`, `.claude/docs/design/spec/federation-oidc.md`):
  built for **other NomNomzBot instances** specifically. Signed envelopes (`rsa-sha256`), manual
  peer-trust promotion (pending → trusted), narrow fixed scope
  (`shared_chat_bans` | `shared_ban_list` | `shared_trust_list` | `shared_savings`). Still real
  onboarding (peer registration + an explicit trust step), just not per-channel Twitch OAuth. Does not
  cover third-party (non-NomNomzBot) bots, and has no concept of mutable shared game state.
- **Outbound webhooks** (`.claude/docs/design/spec/webhooks.md`, shipped): a channel owner registers an
  external URL and picks event types; NomNomzBot pushes signed events to it. Already lets any bot
  receive our events without touching our auth. No pull/read side, no write side.
- **Twitch OAuth scopes are already feature-gated, not "everything on."** Verified in
  `server/src/NomNomzBot.Infrastructure/Identity/TwitchLoginScopes.cs`: a broadcaster login requests
  only 3 scopes up front (read chat, send chat, "channels you moderate"). Every other scope
  (subscriptions, raids, polls, moderation, VIPs, …) is requested **only when that feature is turned
  on**, one delta at a time, per `FeatureScopeMap.cs`. The comment on `TwitchScopeRegistry.FullCatalogue`
  confirms this was deliberate: requesting the full scope catalogue up front made Twitch's own
  authorize endpoint 502 (request too large). So Stoney's starting worry ("I think everything is on by
  default regardless of whether it's needed") does not hold for Twitch scopes specifically — this part
  is already solved and can be reused as the pattern for the new system, not rebuilt.

## 3. The core insight that reframed everything

Stoney gave two illustrative (his words: "dumb") examples:

- **Feather-steal**: he shares a game with aaoa and kitte. A third streamer, kani_dev, joins with their
  own bot/overlay system. Worry: if kani's bot can write the shared state directly, kani could just set
  "user X now holds the feather on kani's stream" without ever legitimately taking it — i.e. cheat via
  the API instead of playing the game.
- **Hot potato**: a potato thrown into kani's stream; someone must throw it on to another person or
  another channel. Same hole: whoever can write "it's now in channel Y" can just teleport it there.

The resolution (confirmed by Stoney as correct, see §4): **no participant ever writes state directly.**
A participant submits an **action request** ("I want to grab the feather", "I'm throwing it to channel
Y"). Only the game's **current authority** (the channel that currently owns/hosts that piece of state)
validates the request against the game's actual rules and applies it. This is the same shape the rest
of the codebase already uses for federation (§2): a federated message is a *claim*, never a *verdict* —
local logic always decides.

## 4. The unifying shape (confirmed by Stoney)

Asked directly whether Hot Potato / Feather-steal / an avatar-with-presence idea were three different
systems or one, Stoney confirmed: **one system.** A single primitive —

> a **portable, owned entity**: has exactly one current host channel at any moment, carries its own
> state blob, and can only be mutated by whichever channel currently hosts it. Moving it to another
> channel is a handoff request the *receiving* channel's authority accepts or rejects.

— with different games/uses riding on top as different state shapes + rule sets:

- Hot Potato / Feather-steal: the entity is an object; the rule set is "who may grab/throw it, and when."
- Avatar + cross-channel presence (TBD's idea, via Stoney): the entity is a viewer's avatar. When
  tbdgamer lurks on Stoney's stream then appears on kani's, the avatar's host channel changes, and the
  old host may require an "unlurk" before the new host accepts it. A channel-points-bought cosmetic item
  rides inside the avatar's state blob, so it travels for free once the avatar does — no separate
  cross-channel inventory system needed, it's just a field.

## 5. The nine (now corrected) layers of the full picture

Written out with Stoney so narrowing scope later doesn't silently reshape the whole thing. Confirmed
mostly right; one correction below (§5, point 6/8).

1. **A "game" becomes a first-class thing** — same tier as Commands, Pipelines, Overlays today. Each
   game instance has its own state.
2. **State always exists.** It does not reset when the streamer goes offline. It sleeps (no timers/
   activity) and resumes when the streamer goes live again.
3. **Something validates every action; nothing writes raw state.** The referee model from §3.
4. **Game logic likely lives in Scripts/Pipelines** — Stoney's own instinct, with an open question
   attached (§6): how far the existing Scripts/pipeline system can really go for someone to build a full
   game in it. Not yet answered.
5. **A game can render as an overlay** — reuse the existing overlay/widget system (the one fixed earlier
   today, same session).
6. **A game is not locked to our overlay, for people who already onboarded.** Full external access: a
   real API, webhook push, a typed SDK. **Correction (Stoney, verbatim-ish): outsiders cannot CREATE
   games.** To create/host a game, an operator must go through real onboarding — full bot onboarding, or
   possibly a lighter "game-creator only" onboarding tier (open question, §6). Outsiders CAN be let in to
   **join** an existing game, with a much lighter credential than full onboarding.
7. **A game's state (or part of it) can move between channels** — the portable-entity handoff from §4.
8. **Access is granted per game, not globally.** A joining bot gets a credential scoped to exactly one
   game, naming exactly which actions it may request — different permissions for Hot Potato than for
   Feather-steal, chosen by that game's owner.
9. **The SDK needs to be genuinely good**, not a nice-to-have, since outside operators will build real
   things against it. Directly relevant side-finding, logged here so it isn't lost: **the SDK has no
   autocomplete for string-based lookups** (pipeline names, reward names, and other loose string-keyed
   values) — a standalone usability gap, worth its own fix regardless of this feature.

## 6. Open questions — NOT decided yet

These block writing an implementable spec. Listed so the next session picks the conversation back up
here instead of re-deriving it.

- **Blast-radius containment is non-negotiable, but the mechanism isn't designed yet.** Stoney's real
  worry (translated): "we hold real authority for a lot of different users' accounts; if there's a leak
  or a security bug, someone could do all kinds of damage across every onboarded account." This is a
  real, pre-existing systemic risk (separate from Twitch scopes, which are fine — §2), and letting
  external bots join games is a brand-new door into the same house. The per-game scoped credential
  (§5.8) is the proposed containment, but it must be a hard wall (a leaked game credential can reach
  *only* that one game, nothing else) — the actual enforcement mechanism (how the credential is scoped,
  checked, and isolated at the data layer) is not designed.
- **How far can Scripts/Pipelines really go** for a streamer to author a full game's rules? Needs an
  honest answer before committing to "game logic lives there" (§5.4).
- **Exact shape of the "game-creator only" lighter onboarding tier** (§5.6) — does it exist as a real
  partial-onboarding path, or is it full-onboarding-or-nothing after all?
- **Credential scope: per participating channel, or per individual viewer across channels?** Raised and
  left open — the avatar/presence idea (§4) implies a viewer has one identity that can appear in
  multiple channels, which is a cross-channel identity/user-mapping problem bigger than "one credential
  per joined bot."
- **No MVP has been chosen.** Stoney wants the full picture settled first specifically so a later MVP
  doesn't force a reshape. A first concrete slice (earlier suggestion: Hot Potato between Stoney, aaoa,
  kitte only, no open ecosystem) was proposed but not confirmed as the actual starting point.

## 7. Next step

Resume the interview from §6. Do not start implementation, a spec in `.claude/docs/design/spec/`, or any
schema/entity work from this document — it is discovery notes, not a locked design.
