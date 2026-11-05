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

## 8. Round 2, same day — Claude's gaps + Stoney's answers

Asked "what would you add?" Claude flagged six gaps; Stoney answered all of them, plus added a scope
decision that reframes §6's first open question.

- **Cross-deployment / open ecosystem — decided.** Stoney, verbatim-ish: "I don't want to limit myself
  to just the 6 [channels] or other NomNomzBot instances. I want an open system anyone can join, hooking
  up their own systems — we become a game provider others can build on." This resolves §6's first open
  question: **yes, cross-deployment, open to anyone, not just other NomNomzBot instances.** This makes
  the Federation-style signed cross-instance trust pattern (§2) the right starting reference, though
  likely needs to be lighter/more self-service than federation's manual peer-trust ceremony, since
  "anyone can join" implies low-friction registration, not a hand-approved peer list. Exact trust
  ceremony for an open, self-service, cross-deployment join is now the new open question (§10).
- **Offline-channel behavior is game logic, not platform logic — decided.** What happens when the
  channel currently holding an entity goes offline is for **that game's own rules to decide**, not a
  platform-wide fixed rule (e.g. not a hardcoded "auto-return after N minutes"). The platform's job is to
  give the game's logic a hook/event to react to ("host went offline") and let the game decide. This
  raises the bar on §5.4 (game logic in Scripts/Pipelines): it must be able to express lifecycle rules
  like this, not just validate individual actions.
- **Simultaneous conflicting actions — decided.** First-come-first-serve.
- **Revocation mid-game — decided, same pattern as offline-handling.** When a joined participant
  ("client") is kicked, the platform reclaims the entity it was holding. What happens to it next — hand
  it to another live channel, or return it to the game's owner — **is the game maker's choice, not a
  fixed platform rule.** Same shape as the offline-channel answer: platform provides the reclaim
  mechanism, game logic decides the policy.
- **Audit trail — decided, and wants it richer than a log.** Not just "who did what, when." Stoney wants
  real analytics for the game owner: who participates, how often each action happens, full insight into
  "what your viewers are up to in your game" — explicitly compared to Twitch analytics (which he
  separately thinks need expanding too, noted but out of scope here). So: the `EventJournal` + new
  `Source` value from §5 point 5 is the raw feed: build a participation/frequency analytics layer on top
  of it for the game owner, not just a raw audit log viewer.
- **Read-only tier — Stoney asked Claude for the concrete shape; Claude's answer, not yet confirmed by
  Stoney:** reuse the pattern the overlay system already has twice, instead of inventing a new one.
  - *Public/link-based read* (no Twitch sign-in): current-state snapshot only, same trust level as an
    overlay URL — unauthenticated but secret, revocable by the game owner. Good enough for a public
    leaderboard embed or companion site.
  - *Signed-in read* (Twitch OAuth, no write scope): same data, but the game can answer "for you
    specifically" — your own cooldown, your own inventory, whether you personally can act right now —
    without needing write access.
  - *Credentialed write* (the join-flow, §5.8): can actually propose actions.

  This needs Stoney's confirmation, not just Claude's proposal — flagged in §10.

## 9. Design philosophy (pin this — it decides every future scope question)

Stoney, 2026-11-05, in his own words (translated, kept close to verbatim because the tone is load-bearing):
"This is a pretty big scope, and thinking this far ahead already makes me a little crazy. But that's just
what I do. Everything I do is NoMercy. Fuck it, we just do it. If it doesn't go the way it should, it has
to go the way it goes. I'm not sane, but I love it. I love my bot, I love streaming. The fair-queue music
system is genuinely amazing — it has the highest interaction of my whole stream, just because people get
to put their own music taste into it, and that keeps them coming back. Hugging, spanking, all those silly
things — people love it and I love it too. So I want more shenanigans. I want to go deep, I don't want to
take the easy shortcut every other company takes because it doesn't pay off enough ROI. That's not how I
see this. I want to think further than others, just because I enjoy it."

**What this means for every open question below and every future one:** when a choice is between the
safe/minimal version and the ambitious/generous version, and there's no hard technical or security reason
to pick the safe one, **default to the ambitious one.** The fair-queue system and the shenanigans commands
are cited as proof this isn't naive — depth and generosity already measurably drive this product's best
engagement. The one hard constraint that does NOT bend for this philosophy is §6's blast-radius
containment requirement (a leaked/malicious credential must stay contained to one game) — ambition on
scope, not on the security wall.

## 10. Updated open questions (supersedes the resolved parts of §6)

Still open, now sharper:

- **Exact trust/registration ceremony for an open, self-service, cross-deployment join.** "Anyone can
  join" (§8) plus "blast radius must stay contained" (§6/§9) together mean: low-friction registration
  (not a manually-approved peer list like Federation today) but still hard per-game scope containment.
  What does signing up as a "game provider" participant actually look like end to end?
  - Stoney, 2026-11-05 (new, answers part of this): "I was thinking of a client-credentials flow where
    any Twitch streamer can register as a developer and get the user's permission for what they're
    allowed to do" — but he flagged his own doubt via the feather/hot-potato cheat scenarios (§3), which
    is what produced the referee model. So: client-credentials-style registration for the *developer*,
    but the resulting token can still only ever *propose* actions, never write state directly — the
    registration flow and the referee model are not in tension, they answer different questions (who can
    ask to participate vs. what they're allowed to do once in).
- **How far Scripts/Pipelines can really go** for a streamer to author full game rules, including the
  lifecycle hooks now required by §8 (host-offline, participant-revoked) — unanswered.
- **The "game-creator only" lighter onboarding tier** (§5.6) — still unconfirmed as a real thing or not.
- **Credential scope: per participating channel, or per individual viewer across channels** (§6) — still
  open; the avatar/presence idea needs this answered.
- **Confirm or correct Claude's three-tier read model** (§8) before it's treated as decided.
- **Still no MVP chosen** — if anything, less likely to be "Hot Potato between 3 channels only" now that
  open-ecosystem is the explicit target from day one; the first slice should probably still be small, but
  needs re-proposing against the now-wider confirmed shape.

## 12. Round 3, same day — provider vs. participant, and the toolkit-not-opinion rule

Stoney, asked to walk through becoming a "game provider" as if he were a streamer friend: **"game
provider" and "participant" are two different roles, not one.**

- **Provider** = creates/owns a game. Must already be a known NomNomzBot entity — self-hosted or SaaS,
  doesn't matter which, that distinction shouldn't matter to this system.
- **Participant** = joins a game someone else created. Can be **anyone, running anything** — no
  NomNomzBot relationship required at all.

This is not a reversal of §8's "open ecosystem" decision, it's the precise shape of it: only onboarded
providers host games; literally anyone can join one.

**The grounding example is real, not hypothetical.** Kani runs a Godot game engine as his entire stream
environment — his code editor is composited into a live 3D scene, his avatar does things, and there's a
running bit where Kani jokes about having "stolen" a lucky feather from Stoney, hanging on his character's
belt, origin unknown (in-joke). **This is where the feather example throughout this document actually
comes from.** Separately, Kani and another streamer already do a manual, bespoke version of cross-stream
presence: when they're talking together, each one's character appears live in the other's stream, audio
routes between them, and a shoutout on one side automatically triggers the same shoutout on the other
side. Stoney wants to build the generic, reusable version of this category of interaction — not just
formalize the feather bit, but make "two streamers' systems play off each other live" a thing any
provider/participant pair can build, without either needing the other's bot.

**Why this matters, in Stoney's words (translated):** "I want to create the freedom to do fun things with
other channels that have their own bot" — explicitly for community/social reasons: the software &
game-dev streaming category is unusually open and social, everyone nerds out together, and he wants
NomNomzBot to be part of enabling more of that, not just a game-state sync mechanism in the abstract.

**Resolved: presence/shoutout-echo is NOT a separate system from the portable-entity game idea.** Asked
directly, Stoney said he isn't sure he wants automatic "echo everything" behavior between linked channels
— but he does want the *freedom* to build things like it. Conclusion: **the platform never hardcodes
cross-channel behavior.** "When X happens on channel A, mirror it on channel B" is itself just a
provider-authored rule-set built on the same core primitive (entity + action-request + subscription), not
a platform feature everyone gets by default.

**This is now a repeating, explicit design rule, confirmed across three separate answers** (offline-
channel handling, §8; revocation handling, §8; echo/presence, this section): **the platform stays a
toolkit. It provides mechanics and hooks. It never bakes in an opinion about what should happen.** Every
future design question should be checked against this rule before being treated as a platform-level
decision.

**Open legal question, flagged honestly, not resolved by Claude:** Stoney wants to prevent third parties
profiting from this without NoMercy Labs also benefiting, "that's part of why we have the license"
(PolyForm Noncommercial). The license clearly covers shipping/redistributing NomNomzBot or code built from
it. Whether it also covers a fully separate product that merely *calls* this API is a greyer case —
API-consumption terms are not automatically the same thing as a code license. This needs real legal
consideration before the open-ecosystem participant flow ships, not an assumption that the existing
license already handles it.

## 13. How this conversation is being run (operational note, not design content)

Stoney, 2026-11-05: write discoveries to this file proactively, without asking permission first, whenever
something important lands — don't wait to be told. Also: for this interview specifically, use plain single
questions in normal text, **not** the structured multi-choice question tool — he's answering by voice
(TTS read-aloud, speech-to-text reply), and a multi-part structured question doesn't work well read aloud.
One clear question at a time.

## 14. Round 4, same day — the holder can be a person, not just a channel

Asked directly, using the feather example: when Kani "has" the feather, is the holder Kani's channel, or a
specific viewer who happens to be on Kani's channel? Stoney: **this is itself a game-logic answer, and for
the feather specifically, the holder is the viewer on Kani's channel, not the channel itself.**

Confirms and sharpens §10/§6's open "credential scope: per channel or per viewer" question, in line with
the toolkit-not-opinion rule (§12): **the platform's "current holder" concept must support either a
channel or a (person, channel) pair, chosen per game — not one fixed shape.** This is not a new identity
primitive to build: a `User` is already a platform-wide identity distinct from a `Channel` (confirmed
earlier the same day investigating the admin console), so "this is the same person across channels" is
already solvable with what exists; the new part is letting a game's state reference a person as the
holder, not just a channel.

## 15. Round 5, same day — the existing Live Games engine, checked, not guessed at

Stoney pushed back on being asked another game-logic question and asked Claude to actually investigate
whether the "million dollar question" (reuse the existing engine, or build new?) has a real answer.
Claude read the engine contract and the full spec it's built from
(`.claude/docs/design/spec/live-games.md`) before answering.

**What already exists:** `ILiveGame` (pure-logic drop-in class: `OnStartAsync`/`OnInputAsync`/
`OnTickAsync`/`OnResolveAsync`), one generic `ILiveGameEngine` + `LiveGameRunner` that runs every game, a
`GameSession` entity, crash-safe JSON state snapshotting, auto-discovery (a new game is one class + one
widget, zero engine edits). Drop Game, Raffle, Heist, Crash are built on it today.

**Why it cannot just be extended — two decisions in the spec are hard, deliberate walls, not gaps:**

- **D7: "At most one non-terminal session per channel."** A session belongs to exactly one channel. No
  concept of a session spanning channels exists.
- **Every session is built to end.** Lifecycle is `lobby → running → resolving → settled/cancelled` —
  resolve-and-finish, not exist-indefinitely-and-sleep.

Also: input is routed from exactly one source, the session's own channel's chat (one keyword match), and
payouts are wired directly into the currency ledger (`SourceType=live_game`). None of this is a small
patch — single-channel scope and a terminal lifecycle are foundational to the schema (`GameSession` has a
single tenant `BroadcasterId` FK) and the runner's model, not an oversight.

**What IS worth reusing — the pattern, not the engine:** pure-logic drop-in class, the engine (not the
game) owns every side effect, auto-discovery so adding a game is zero engine edits, crash-safe JSON state
snapshots. These are good ideas independent of the single-channel/terminal-session assumptions.

**Claude's recommendation:** build a **second, new kind of game next to this one, not inside it.** Same
spirit (pure logic, engine does the plumbing, drop-in, zero engine edits to add one) but its own entity
that is not locked to one channel, does not have to end, and accepts input from outside the channel's own
chat — not a workaround bolted onto `GameSession`'s rules, which were written specifically to rule this
out. Not yet confirmed by Stoney as the final answer — proposed, pending his read.

Stoney confirmed the §15 proposal ("sounds great") and asked: how far can we let someone build a
complicated game in JavaScript, thinking of real web-based JS games and game platforms that exist today?

## 16. Round 6, same day — JavaScript game logic: two different questions, checked against the real sandbox spec

Read `.claude/docs/design/spec/custom-code.md` in full before answering. Splits into two separable
questions with very different ceilings:

- **The visual/client side has no real ceiling.** A game's overlay is already just a browser page — same
  as every overlay today. A provider can build a genuinely rich game there with any web game tech
  (Canvas, WebGL, Phaser, …), same as any real standalone web game. Nothing in this system limits that.
- **The server-side rule logic (the referee, §3) already has a real foundation.** Custom scripts today are
  authored as TypeScript, compiled to JS, and run in a sandbox that is deny-by-default: a script only ever
  gets specific granted capabilities, never a raw credential, URL, or another tenant's data, and every run
  is resource-budgeted (wall-clock time, memory, host-call count, output size). That shape is exactly what
  a game's action-validation logic needs.

**The one real gap, found in the spec, not guessed at:** the sandbox actually running today everywhere
(`JintScriptExecutor`, Jint) is explicitly documented in its own spec as **"NOT a security boundary —
single-operator threat model only."** It protects a channel owner from their own mistake, not from a
hostile third party's code. A real isolation sandbox (`WasmtimeScriptExecutor` — fuel/memory/time limits,
a genuine boundary) **already exists as a built, tested class but is not registered/wired up anywhere.**
For an open ecosystem where many unrelated providers each author game logic that runs on our server, that
is precisely the untrusted-multi-tenant case Wasmtime was built for and Jint explicitly was not.
**Conclusion: turning on the existing Wasmtime path is a prerequisite for letting providers author real
server-side game logic broadly — not a new invention, a known, already-half-finished piece of work that
needs finishing before this ships.**

## 17. Round 7, same day — Stoney agrees turning on Wasmtime is a good idea, asked how it actually works

Stoney: turning it on quickly sounds like a good idea, but wants to understand, in plain terms, how it
actually works and how it concretely stops someone doing something nasty on his system. Answered in
plain voice-friendly language (no AskUserQuestion, per §13), grounded in `WasmtimeScriptExecutor.cs` and
`custom-code.md`, not generic sandboxing claims:

- **Jint today is not a wall.** It runs in the same memory space as the rest of the bot. It protects a
  channel owner from their own mistake, not from hostile code. We only ever use it for scripts we or a
  trusted owner wrote.
- **WASM (Wasmtime) is an actual isolation boundary, not a setting.** The code runs in its own memory
  block, enforced by the processor itself. That code physically cannot reach the bot's memory, the
  filesystem, the network, or another tenant's data — not because a check denies it, but because there
  is no path there to deny.
- **Three resource limits on top of the memory wall:** a fuel budget (every step the script takes costs
  fuel; out of fuel, it stops — no infinite loop can hang the bot), an independent wall-clock watchdog
  (cuts the script off on time even if fuel accounting itself were broken), and a hard memory cap (the
  script's memory block cannot grow past it).
- **The only way out is a narrow, capability-gated bridge.** The script can't act directly — it can only
  call specific host functions we expose ("what's player X's score"), each checked against a
  per-execution allow-list (deny-by-default). Never a raw credential, URL, or another tenant's data —
  only plain values in, plain values out.
- **One deliberate constraint to keep intact:** Wasmtime only runs through Cranelift on x86_64. Winch and
  aarch64-Cranelift stay disabled — a known, unpatched escape risk — so this is one proven path, not
  several, and that must not change casually later.

In one line: Jint is a lock on a door inside the house; Wasmtime is a separate sealed room with no door
to the rest of the house at all.

## 18. Round 8, same day — resuming the interview from §10, game logic shape

Asked: should a provider's game rules live as one script per action type, or one script that owns the
whole game? Stoney: **one entry point where everything is defined** — a single module/script is the
right shape, not one script per action. He does not yet have an opinion on what that entry point's
internal shape (named exports per event vs. one dispatch function, etc.) should look like — flagged as
still open, not avoided.

## 19. Round 9, same day — lifecycle hooks confirmed

Asked: should "host went offline" and "participant was revoked" be separate named moments the entry
point can react to, on top of "an action was requested" — or keep it simpler for now even if those two
cases go unhandled? Stoney: **yes, worth having as separate named hooks.** So the entry point's shape
needs at least: an action-request handler, a host-offline hook, and a participant-revoked hook — three
distinct moments, not one generic callback.

## 20. Round 10, same day — credential scope: per client, Keycloak-shaped

Asked: should the credential represent the connecting bot itself, or an individual viewer? Stoney:
**per-viewer credentials aren't workable at scale — it has to be a client.** His own comparison: like
Keycloak, a registered **client** connects to the bot and is granted its own access, with our system's
permission. This resolves §10's "per channel or per viewer" open question: **the credential boundary is
the client (the connecting bot/system), not the individual viewer.**

This sharpens, but doesn't resolve, §14's finding (the feather's holder is a specific person on Kani's
channel, not Kani's channel itself): a client authenticates once, but then **asserts** which person an
action is for ("this request is from viewer X on my platform") on a per-request basis — the client-level
credential and the per-action person reference are two different layers. Not yet asked: how much the
platform should trust that per-person assertion at face value versus require some proof — flagged for
the next round.

## 21. Round 11, same day — per-viewer identity assertion: trust, scoped by blast radius

Asked whether a client's claim about which viewer an action is for needs proof, or can be trusted as-is.
Stoney: in principle yes, a client could lie, but **how much harm that can actually do depends on what
that specific game can do and who's allowed to join it** — and that's exactly what the per-game
permission system (§6/§9's blast-radius containment, §5.8's per-game scoped credential) is for.
**Decided: trust the client's per-viewer claim at face value; rely on the permission/scope system to
bound the damage, not on verifying each identity claim.** Consistent with the existing rule that ambition
is fine everywhere except the blast-radius wall (§9) — this isn't a new exception, it's that wall doing
its job: a lying client is contained to the one game its credential can touch, same as a malicious one.

## 22. Round 12, same day — no lighter onboarding tier; hosting is a broadcaster feature

Asked whether becoming a provider needs the full existing onboarding or a lighter "just host a game"
signup. Stoney: **this should just be a feature of being a NomNomzBot broadcaster** — no separate
lighter tier. This resolves §10's "game-creator only lighter tier" open question: **it does not exist**;
hosting a game requires the same full onboarding as everything else NomNomzBot already does, same
footing as Commands, Pipelines, Overlays (§5.1).

## 23. Round 13, same day — three-tier read model confirmed

Asked whether the public/signed-in/credentialed-write split still holds. Stoney: **yes, confirmed** —
and the signed-in tier needs no new mechanism, it just rides the existing viewer/user login a normal
viewer already has, giving them their own personal view into the game (their own cooldown, their own
state) for free. This resolves §10's "confirm or correct Claude's three-tier read model" open question:
**confirmed as proposed**, public link / signed-in-via-existing-login / credentialed-write.

## 24. Round 14, same day — MVP: internal first, kittE's hairball-hot-potato idea

Stoney: check what kittE is already doing — she had the idea of a cat-hairball as the "hot potato" item,
not sure if she's already built anything. Proposed shape: **start internal-only** (Stoney + kittE,
own system, no external clients), prove the entity/referee/hook mechanics end to end with her hairball
idea, **then extend outward** to let an external participant (the kani example, §12) join. This confirms
§6/§10's "no MVP chosen" question: **internal-first with kittE's hairball Hot Potato, open-ecosystem
join added as the next step after that, not day one.** Not yet checked: whether kittE has already built
something for this — Claude cannot see kittE's own systems, so this needs Stoney (or kittE directly) to
say what exists, if anything, before scoping the first slice further.

## 25. Round 15, same day — kittE's game, checked directly against the dev database, not guessed at

Stoney: check the real database instead of asking kittE first. Done — read `qtkitte`'s channel
(`Id 019f292e-7327-741f-85e1-44ff086a42e8`) on the dev Postgres box via the custom-code tables.

**She has already built it, and it is live today, single-channel.** Two active `CodeScript`s, both
iterated heavily (29 and 6 versions, last edited 2026-10-26):

- **"Ball Lifecycle"** — fires on Twitch channel-point reward pause/resume/enable/disable. Explicitly
  commented as a port of the **legacy `LuckyFeatherChange.cs`** system. Two items share the same
  lifecycle: "Throw the hairball" (bad item) and "Steal the ball of yarn" (good item). Announces in
  chat + TTS, rewrites the reward's prompt to name the current holder, and emits to a "Kitty Alert"
  overlay widget.
- **"Steal ball of yarn"** — fires on the steal redemption. Reads the current holder from script storage
  (`nnz.api.storage.get('yarn:holder')`, defaulting to the channel itself), blocks stealing from
  yourself (with a 25-line joke pool for trying), otherwise moves it to the thief, bumps the reward's
  cost by 1 per steal (an escalating price — a real anti-runaway-loop mechanic already in production,
  independently of anything in §9's discussion), updates a per-user leaderboard counter, resolves real
  Twitch profile pictures for the overlay, and emits `kitty.steal` to the same widget.

**What this concretely tells us about the MVP (§24):** this is not a blank slate to design from — it is
the actual game this whole conversation has been using as its running example, already live, already
proven out with viewers today. It runs entirely on the *existing* custom-code + Twitch-reward +
script-storage stack (the Jint-based system from §16) and is **single-channel only today**: "holder" is
a plain string in that script's own key-value storage, defaulting to her own channel — it has no concept
of a cross-channel entity yet, because the platform doesn't have one yet.

**This sharpens the MVP to something concrete, not abstract:** the first slice isn't "build Hot Potato
from zero" — it's "take kittE's already-working hairball/yarn item and give its holder a real portable-
entity identity (§4) that can move to Stoney's channel and back," reusing her existing reward/widget/
chat-announcement wiring rather than replacing it. The entry point (§18), the two lifecycle hooks (§19:
host-offline isn't exercised by this single-channel version yet, but revoke/steal already is), and the
referee model (§3) all have a real, running example to be checked against instead of a hypothetical one.

## 26. Round 16, same day — Stoney corrects the framing: a real scaffolded runtime, specced for WASM, not a hand-migration

Stoney: not interested in hand-migrating kittE's scripts case by case. He wants to **define the runtime
itself** — create-a-game gives a scaffolded starting point with entry/exit points, config options, and a
lifecycle system built in, and that runtime contract needs to be fully spec'd so it can run inside WASM
(tying back to §16/§17's Wasmtime finding). This is the TBD idea referenced earlier (§4).

**Checked `dev-platform.md` before answering — found the project already has a real answer to half of
this, but for a different, narrower thing than what this conversation has been designing.**
`dev-platform.md` §6/§9 already specs a **"Scripted games"** phase (phase 5, status **Open**, not yet
built): `nnz.manifest.json` already supports `kind: widget|game|script` today, a project is already a
file-set + manifest + multi-file build (esbuild, typed editor, forkable-from-gallery — all **already
built** for scripts and widgets), and the **planned** `kind: game` shape is `ILiveGame` gaining a
`ScriptedGame` implementation that delegates `onJoin/onTick/onResolve` to a sandboxed script. That is
real, designed, scaffolding-ready infrastructure — but it is explicitly built **on top of the existing
single-channel `GameSession`/`LiveGameRunner`** (round 15's D7 wall), with the existing 3-phase hook
set (`onJoin/onTick/onResolve`), not the cross-channel portable-entity system this conversation has been
designing (whose hooks are `onAction`/`onHostOffline`/`onParticipantRevoked`, round 18/19).

**So there are now two "scripted game" ideas on the table, not one, and they need to be reconciled, not
silently merged:**

1. **dev-platform.md's existing plan** — scripted single-channel games (Drop Game-style), forkable from
   a first-party gallery, scaffolded via the existing manifest/project/build pipeline. Already designed.
2. **This conversation's cross-channel portable-entity games** (kittE's hairball/yarn, Hot Potato) — a
   new sibling engine (round 15), new hook set, new credential/trust model (rounds 20-22) layered on top.

**The right move, not yet confirmed by Stoney:** reuse the *scaffolding machinery* from (1) — the
manifest shape, multi-file project model, typed SDK, build pipeline, forkable gallery — for (2) as well,
rather than building a second, separate scaffolding system. Concretely: a new manifest `kind` (e.g.
`kind: shared-game`, name TBD) alongside the existing `widget|game|script`, whose "entry point" contract
is the three hooks from round 18/19 instead of `onJoin/onTick/onResolve`. Same create-a-game scaffold
experience Stoney is asking for, same WASM-hosted execution path (round 16/17's Wasmtime prerequisite
applies identically to both), different hook contract because the underlying session model is genuinely
different (round 15).

## 27. Next step

Resume the interview from §10 and the proposal in §15. Do not start implementation, a spec in
`.claude/docs/design/spec/`, or any schema/entity work from this document — it is discovery notes, not a
locked design.
