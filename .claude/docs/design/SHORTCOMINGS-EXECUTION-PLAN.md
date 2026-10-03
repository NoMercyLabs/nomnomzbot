# Execution plan — the slice index (one ordered queue, top to bottom)

Binding inputs: `PRODUCT-ALIGNMENT.md` (decisions D1–D12) · findings in
`stability-audit-scope-and-plan.md` (**S**·F1–F19), `widget-quality-audit-scope-and-plan.md`
(**W**·§1–§8), `usability-shortcomings-audit-scope-and-plan.md` (**U**·A1–A7, B1–B7, C0–C7),
`usability-inventory-2026-09-24-audit-scope-and-plan.md` (**V**·A1–A6, B1–B8, Part C),
`sleak-review-2026-08-22.md` (**K**). This file only orders them.

**How to execute:** one slice at a time, in this order; each slice is the smallest testable vertical
cut (contract → service → data → UI where it applies → test). Test-first, locally (`tdd-local-no-ci`);
commit when the **Done-when** is proven; then **delete the slice from this file** (tracker = remaining
work only). A slice may be split further while executing, never merged. 🔒 = needs the owner first;
skip it and continue. Persona priority (owner): streamer → moderator of many → viewer.

**Ordering rule (owner, 2026-08-22): stabilize the CURRENT feature set first; merge new code only
where a fix requires it; add the new stuff after.** Phases: 0 truth-and-safety · 1 runtime stability ·
2 existing platforms made to work (minimal spine) · 3 form infrastructure · 4 existing-feature truth/
reach · 5 new model (one channel, many platforms; any login) · 6 new features + personas · 7 polish.
Slice IDs are stable; the order is the queue.

---

## OWNER REQUEST 2026-10-02 — code editor 100% type safe, SDK reliable and easy (in progress, top of queue)

Owner: "get the code editor to be 100% type safe and ensure the SDK is reliable and easy to use and
understand" + "a ton of tests to fire so scripts can be tested on their logic and output — every OBS and
chat exposable visible from the preview window". The draft user docs live in `docs/sdk/` (one topic per
page); `docs/sdk/help/known-problems.md` is the defect list for the slices below. Fix order: the
defects first, then S-SDK-DOCS-ATLAS documents the fixed code.

- **S-SDK-WIDGET-DELIVERY** (stream-facing, first) Each event reaches its subscribers once, and every
  sound and TTS line plays on one page only (e95e7417d: newest Audio Source page, else newest overlay
  page). Owner decision 2026-10-02: "lets have just one audio source for tss and all other scripts and
  audio fragments, and have volume control handled on the bots side so balance stays static across
  multiple streaming pc's used by that user". Shipped: bot-side master and TTS volume (ca32f9f63,
  defb69885), the `play_sound` handle, and the inbox notice when no Audio Source page is open
  (`AudioSourceMissingSource.cs`). Left: the live check. Done-when: tests prove each, and on the deployed site one
  TTS line and one sound clip play once with a caption page and an Audio Source page both open.
- **S-ACTOR-PLATFORM** (stream-facing) A chat-triggered actor reaches a pipeline as a bare platform user id
  (PipelineExecutionContext.TriggeredByUserId; ChatMessageHandler.cs sets it from the chat event) with no
  platform. So a lookup of the internal user can only assume Twitch: MusicService.ResolveInternalUserIdAsync
  and ModerationProjectionService.cs:245 both match Users.TwitchUserId, and a Kick or YouTube chatter
  resolves to nobody. Done-when: the context carries the platform with the id, every internal-user lookup goes
  through IUserIdentityService.ResolveUserAsync(provider, id), and a test with a Kick chatter skipping a song
  publishes their internal id.
- **S-SDK-EDITOR-FRAMEWORKS** `.vue` and React files are type-checked in the editor; a failed SDK-types
  fetch shows a notice instead of silently untyped code; the create dialog stops offering svelte, which
  the build refuses (`WidgetsScreen.kt:1288` vs `EsbuildWidgetBuildService.cs:116-121`).
- **S-SDK-DOCS-ATLAS** Owner 2026-10-02: the SDK docs are written with the **atlas** skill (map, scanned
  source, two reviews per page, `check_docs.py status` = DELIVERED), for streamers who know no
  programming, one topic per page. The generic drafts now in `docs/sdk/` are existing documentation to
  audit (atlas Phase 4), not the delivery. Runs after the SDK fixes above so no page documents a bug.
- **S-EDITOR-I18N** The web code editor (`server/src/NomNomzBot.Api/Assets/editor/editor.js` and
  `index.html`) writes every label in English inside the bundle ("Run test", "Running…", panel titles,
  status text), so a Dutch dashboard shows an English editor. The gallery widget names and
  descriptions in `FirstPartyWidgetCatalogue.cs` are English literals in the same way. Done-when: the
  editor takes its labels from the app's resource strings (en and nl) in the open message, the gallery
  names and descriptions resolve through resource keys, and a test fails when a bundle label has no key.
---

## OWNER PUNCH LIST 2026-09-10 — remaining follow-ups

- **S-STREAMDECK-OBS-ICONS** Stream Deck OBS plugin icon art — Done-when: every OBS action ships its
  own key icon in the obs manifest
- **S-MOD-BAN-UNSEEN** (owner todo 2026-10-01, not now) The Moderation page's "Moderate viewer" dialog
  only finds viewers the bot has already seen (`searchViewers`). For a Twitch login that never chatted
  (`twrtlebeach`) it shows "No matching viewers" plus "Enter a Twitch user ID", so the streamer cannot
  pre-ban a known troll. Done-when: when no known viewer matches, the dialog looks up the typed login on
  the platform (Twitch Helix Get Users by login), shows that account (name, avatar, created date) as a
  pickable result, and ban / timeout applies to its platform id; a login that does not exist on Twitch
  says so.
- **S-SR-ROLE-LIMIT** (owner idea 2026-10-01: "per role song queue limit", filed for later) The song
  queue has one cap for everyone: `MusicConfig.MaxRequestsPerUser` (default 5), enforced as
  `PER_USER_LIMIT`. Done-when: the streamer sets a separate cap per role (viewer, follower, subscriber,
  VIP, moderator, broadcaster) on the music settings page; a requester gets the highest cap of the roles
  they hold; the refusal reply names their cap; a role without its own value falls back to the channel
  cap.
---

## OWNER BUG 2026-09-04 (b) — `!sr` answers with the PREVIOUS request's track (parked)

- [ ] **S-SR-STALE** (parked) Owner saw `!sr 9 to 5` answered with the track for the preceding
  `!sr joliene`; four layers are eliminated and it does not reproduce. Done-when: on a fresh
  sighting the exact outgoing Spotify query is logged beside the message id that triggered it, and
  the cause is fixed.

## OWNER REQUEST 2026-09-04 (b) — the admin plane a SaaS owner actually operates from

Owner, verbatim: "add a slice that completely overhauls the admin dashboard and gives me every tool i could
ever need managing the saas, including the editing of system commands, widgets, pipelines and code scripts.
and i mean everything i could ever need to configure as a saas owner/admin".

> **`saas` mode is a RESTRICTED option** — operating NomNomzBot as a hosted service for third parties is
> reserved to NoMercy Labs under the licence. Self-hosting your own bot is always free. Everything below is
> the operator surface for that restricted mode and must keep carrying this marker.

**Measured today (2026-09-04), not assumed.** The admin plane is **11 tabs** — Overview, Channels, Users,
System, Billing, IAM, Tenants, Audit, Spam Defaults, Providers — across 2,732 lines. Backing it: 10 admin
controllers (`AdminController`, `PlatformAdminController` with 9 routes, `PlatformIamController`,
`AdminBillingController`, `FeatureFlagAdminController`, `PlatformAnalyticsController`,
`AdminSpamDefenseController`, …).

**The structural gap, and it is the owner's exact ask.** Every piece of content the platform ships to every
tenant is seeded from **C# code**: `DefaultCommandsSeeder`, `FirstPartyWidgetCatalogueSeeder`,
`RaidFlowSeeder` / `RaidStartFlowSeeder` / `RaidCommitFlowSeeder`, `EventResponseDefaultsSeeder`,
`TtsVoiceSeeder`, `BillingTierSeeder`, `IamCatalogSeeder`, `ActionDefinitionSeeder`, `PronounSeeder`,
`ConfigSeeder` — 17 seeders in all. Per-tenant editors exist for all of it (`CommandsController`,
`WidgetsController`, `PipelinesController`, `CodeScriptsController`, `BuiltinsController`,
`TemplatesController`, `BundlesController`, `MarketplaceController`, `CatalogController`), but **nothing
edits the platform-level originals**. Changing one default command today means editing C#, building, and
deploying — the SaaS owner cannot author the product they are selling.

**Settle the spec before writing code** (house rule). Two questions decide the whole shape and neither has an
obvious answer, so they get decided FIRST, in the spec, with the reasoning written down:

1. **Propagation.** The owner edits a system command that 400 tenants already have. What happens to a tenant
   who renamed it? Who edited its response? Who deleted it? The existing seeder principle — *adopt an empty
   stub, never overwrite built content, never match on name alone* — is the starting point, but "the owner
   fixed a broken default and wants it to actually reach people" is a real need that principle refuses. The
   answer is probably per-edit (`publish as new` / `update in place where untouched` / `force`), and it must
   be visible in the UI before the owner commits, per the standing rule that consequences are shown.
2. **Seed vs template vs live.** Is platform content a row the seeder writes, a template tenants instantiate,
   or a live reference tenants point at? This decides whether an owner edit is a migration, a fan-out job, or
   nothing at all. Pick one and say why the other two lose.

Spec settled in `spec/platform-admin.md` (`93289b10`): propagation = per-publish mode with a counted
blast-radius preview; platform content is a TEMPLATE tenants instantiate. S-ADMIN-2 splits by content
kind on that spine: **2a** spine + system commands CLOSED (`78f816eb`), **2b** the admin surface for
them CLOSED (`6b342371`), **2c** widget kind on the spine CLOSED (`debe2e42`). Remaining: **2c-b** make
the widget publish actually reach a viewer, **2d** system pipelines, **2e** code scripts — each reusing
the real tenant-side editor, never a second worse one.

**S-ADMIN-2c-c CLOSED (`b2111281`).** The Content tab now authors the widget kind — editable Vue
source, settings schema and event subscriptions — and a publish job with failed tenant rebuilds
renders a visible failure line naming the count instead of leaving `RebuildFailedWidgetIds` unread.
No backend change was needed: `payloadJson` already carries source/settings/subscriptions end to end
and `WidgetContentPayload.TryParse` validates it server-side at publish. Verified independently, and
the failure-surface test asserts the rendered count string, so it fails if the field is dropped.

2a shipped the entities, BOTH migration sets (proven on a POPULATED database via `migration-check.ps1`,
not an empty one), the publish engine and `PlatformContentController`'s 9 routes. Two guards it added
beyond the brief and worth keeping: a force publish without a justification is REJECTED, and a publish
carrying a stale preview count fails closed rather than fanning out against numbers the owner never saw.

**S-ADMIN-2 CLOSED — the owner's explicit ask is delivered.** All four content kinds are authored at
platform level on one spine, each reusing the REAL tenant editor rather than a second worse one:
system commands (`78f816eb`/`6b342371`), first-party widgets (`debe2e42`/`b2111281`), system pipelines
through the shared tree editor (`84e6c9be`/`8010cda2`), and sandboxed code scripts through the shared
project editor (`ddbf75bf`). Every kind: a counted blast-radius preview, a stale count failing closed,
a force publish rejected without a justification, and propagation proven as per-tenant STATE —
untouched receives, customised is never overwritten, deleted is never resurrected.
`Publish_PlatformPublishedScript_RunsUnderTheSameSandboxLimit_AsATenantAuthoredScript` hits the real
Jint statement-limit wall identically for a platform-published and a tenant-authored script, so
publishing from the platform grants no wider powers. The Content tab's kind header now names each
row's actual kind instead of always saying "Command".

**CLOSED (`f04bf82c`).** The widget kind now opens its Vue source through the same `ProjectEditorIO`
overlay (entry `index.vue`, language `vue`), so all four content kinds author through the real tenant
editor. Settings schema and event subscriptions were asserted to still round-trip beside it — a silent
regression there would have broken widget publishing.

**S-ADMIN-4a CLOSED (`d191bc3e`, label + contract follow-up `365392eb`).** Tiers and prices are
authored from the admin plane: the edit persists and writes an audit entry, the blast-radius preview
returns the real counted number of tenants on the tier, and an apply carrying a stale confirmed count
fails closed rather than fanning out against numbers the owner never saw — three Api.Tests, plus
`AdminBillingTierEditorRenderTest` green on the module.

**S-ADMIN-4b CLOSED (`b36f42b3`), verified.** Comps and per-tenant entitlement grants, with both
migration sets and the snapshot regenerated through the script. The grant is proven to be read by the
REAL gate, not a parallel path: `RequireTierAction` calls `IBillingTierService.IsTierAtLeastAsync`,
which is the exact method the test drives on a real `BillingTierService` — false before the grant,
true after — and the expiry test asserts the capability is DENIED once expired, not merely that a
field was read. The admin surface renders reason and expiry and shows the counted blast radius before
the issue button enables.
**S-ADMIN-4c CLOSED (`e0e766be`).** Invoices list with their real shape and ordering, refunds record
the refunded amount and audit the actor with a second refund of the same invoice REJECTED rather than
double-paying, and dunning is derived through one shared `Invoice.ResolveDunningStatus` — the same
path the rest of the system reads, not a decorative badge. Both migration sets; snapshot regenerated
through the script; the tab shows the counted amount before the destructive button commits.

**Format-string trap found and fixed (`18c514de`).** `admin_tier_price` spelled the minor units
`%2$02d`, which reads correct and is not: compose-resources ignores width and flag specifiers on a
POSITIONAL argument, so a price of 10.05 rendered as 10.5 and had shipped that way since the tier
editor landed. Nothing rendered that string in a test. Padding now happens in Kotlin, and a test
asserts the rendered text — reverting the fix makes it fail. Both string tables swept: the only other
positional flag is `%4$+d` in `games_history_row`, which no call site uses.
**S-ADMIN-5 CLOSED (`2bfad340`).** Per-tenant and per-cohort flags, a staged percentage rollout and
per-integration kill switches — all proven by enforcement, not persistence: a kill switch disables
only the killed tenant, a tenant lands on the same side of a partial rollout every time, a 30%
rollout admits 25–35% of 5000 tenants, and an evaluation failure degrades to disabled instead of
throwing. The tab renders cohort percentage and per-tenant override state, shows the counted blast
radius before a kill switch commits, and a cancelled confirm leaves the flag untouched.
**S-ADMIN-6a CLOSED (`e8edf91a`, follow-ups `5691cb4c`).** Per-tenant EventSub subscription health read
from the REAL registry — a test with two tenants, one revoked, distinguishes them and the output moves
when the underlying subscription state moves — and an outbound webhook delivery log whose replay
performs a genuine new send, appends a NEW attempt row leaving the original intact, audits the actor,
and is REJECTED when the endpoint has since been deleted or disabled.

Two defects it exposed, both fixed rather than filed: the tenant-facing `RetryDeliveryAsync` still did
`delivery.Attempt++` and re-sent the same row, overwriting the failed status and response code the
operator was looking at when they pressed retry — it now delegates to the same append-only replay, so
there is one rule and not two; and `EntitlementGrant` (added in `b36f42b3`) was never classified into a
blast-radius category, which left `ChannelBlastRadiusSourcesCompletenessTests` red on master and would
have silently dropped that table from the count shown before a channel delete.

**S-ADMIN-6b CLOSED (`ceb9b7c4`), verified.** Background job queue with retry, and per-tenant usage.
Built on `ScheduledPipelineTask` — the real persisted dispatch primitive already swept by
`ScheduledPipelineExpiryService` — rather than inventing a parallel queue, because the codebase has no
generic background-job entity. Retry appends a new attempt and leaves the original failed one readable
(the bug shape fixed for webhooks in `5691cb4c`, proven catchable here), and is REJECTED when the job
already succeeded or its target pipeline is gone.

**It deliberately shows no cost figure, and that is the correct answer, not a gap in the slice.**
Per-tenant usage reports real measured quantities from `UsageRecord` + `TtsUsageRecord` with the period
stated. There is no per-unit price table anywhere in the codebase, so any currency number would have
been fabricated — which the never-show-unmeasured-state law forbids.

**S-ADMIN-4d CLOSED (`97a25e60`) — S-ADMIN is now complete, 2 through 9.** The owner authors a
priced-unit catalogue (integer minor units + explicit currency + a batch size, so a real
sub-minor-unit rate prices exactly in integers). It ships EMPTY on purpose: a unit with no row is
UNPRICED, and unpriced is not zero — usage still reports the measured quantity, reports cost only for
priced units, and NAMES the unpriced ones. The render test asserts BOTH unpriced lines say so, so
neither can quietly fall back to a zero. Repricing is deliberately not retroactive: cost is computed
live against the current rate, because this is an operator cost view, not an invoice ledger — chosen
and asserted rather than left implicit. Both migration sets; snapshot regenerated from a running API
(604 paths); Infrastructure 5170, Api 935, jvmTest and wasm green.

**S-ADMIN-6c CLOSED (`a785f8ce`) — S-ADMIN-6 is complete.** Error budget computed from real delivery
outcomes (it MOVES when the underlying records move) and scoped per-tenant, matching the
`GetTenantUsageAsync` precedent: a 2am tool exists to find WHICH tenant's integration is broken, so one
blended platform number would defeat its purpose. Event-store replay re-applies matched `EventJournal`
rows through each projection's own real `ApplyAsync` — the live driver's per-projection fold, not a
second engine — with a counted preview scoped to the projection's subscribed types, a stale count
failing closed and applying nothing, and an audit row naming operator, scope and count. Replay is
**idempotent** rather than append-only: `IProjection.ApplyAsync` is contractually an upsert keyed on
`EventId`, so a second identical run re-applies the same upserts instead of doubling state.
**S-ADMIN-7 CLOSED — 7a `0c6d017f`, 7b `922073d7`, both verified.** Cross-tenant person search and a
person view of real state (roles, standings, trust, entitlements, connections, each labelled with the
tenant it belongs to), plus history replayed from the real event journal. Gated on the new
`user:support:view` key threaded through all five places a key lives; every lookup requires a
justification and is audited naming operator and subject — denials audited too, and proven to read no
subject data.

The tests were checked for teeth, not just for green: the two-tenant history test asserts
EventType/BroadcasterId/ChannelName per index in a fixed newest-first order (it fails on a swap or a
reversed order) and seeds a THIRD person's event to prove no cross-person leak; the refusal test
asserts `FORBIDDEN` plus a real Denied audit row, so it would not pass against an endpoint that merely
returned an empty list; a fact the system does not hold renders absent, never as a zero row. The
shared `AuthTestContext` change was inspected separately because a loosened harness can manufacture
green: it only maps the previously-ignored `EventJournal` DbSet to its real production configuration —
strictly additive — and the full 5119-test suite across ~150 files sharing that context stayed green.
**S-ADMIN-8a CLOSED (`d1a9b897`).** Cross-tenant abuse signals computed from real recorded detections
(the aggregate names the right actor, tenants and detections, and an unrelated actor is excluded), and
a review queue for the platform's automatic calls where an overturn calls the real unban FIRST and
only then marks and audits — with a failed reversal leaving the detection untouched, so "marked
overturned but still banned" is impossible.

**It exposed a defect class with three members, all now fixed.** A reversal that stamps a row without
performing the undo is the never-show-unenforced-state law broken on a user-facing path:
1. platform review queue — correct by construction (`d1a9b897`);
2. per-channel `SpamDefenseService.OverturnDetectionAsync` — stamped `OverturnedAt` and never unbanned,
   so a moderator's overturn left the viewer banned. Fixed `5e2d68e0`, proven red-first;
3. `SpamCorrelationService` campaign de-qualification — stamped `ReversedAt` while its only consumer
   merely LOGGED `AccountsToRestore`, so a whole false-positive cohort stayed actioned. Fixed
   `20694769`: every account is attempted, but `ReversedAt` is written only when all were restored; a
   partial restore keeps `ReversedAt` null and persists `RestoredAccountCount` +
   `RestorationFailedAccountIds` so the shortfall is data, never silence.
The rest of the class was traced, not assumed: `AgeConsentService`, `LiveGameEngine`, `LiveGameRunner`,
`ModerationQueueService`, `ViewerReportService` and `TrustSafetyReviewService` all stamp only after
their real action. Search: `ReversedAt =|OverturnedAt =|ConfirmedAt =|ResolvedAt =` then trace consumers.

**S-ADMIN-8b CLOSED (`5e227dea`), verified against its safety properties, not just its tests.**
Network-wide blocks ship with: a counted preview naming the real tenants; an apply carrying a STALE
count failing closed with zero bans issued and zero rows written (verified — a refusal that had
already fanned out half the bans is the dangerous case); enforcement in `RoleResolver.HasCapabilityAsync`
itself, so the block is consulted on the real production path in a tenant the actor was never
individually blocked in, not merely visible to the admin surface; a lift that unbans every tenant the
apply touched and, on a failing leg, leaves `Status` non-Lifted with `LiftedAt` null and the failed
tenant named, still enforced; a required justification; and ownership proven in BOTH directions —
platform-super-admin is explicitly excluded, only the narrow named role grants it.
The two blast-radius edits were checked separately: the `RoleResolver` diff is a strict early
deny-return that cannot widen any permission, and all 52 fake-DbContext edits are additive
`NetworkBlocks` stubs, not assertions bent to pass.

Cross-INSTANCE signature sharing stays blocked (needs a NoMercy service that does not exist).

**S-MOD-REVERSAL-VISIBLE CLOSED (`b165fe50`).** A campaign's reversal outcome is now legible: three
states render distinguishably — "Reversal incomplete - N accounts still actioned", "Undone: <reason>",
and nothing when never reversed — so a partially-restored cohort can no longer look clean.
**S-ADMIN-9 CLOSED (`cdca15cf`).** The plane had grown to 18 surfaces; it is now five job groups —
**Live** (Overview, EventSub health, Webhook deliveries, Job queue, Usage, Audit) · **Who** (Channels,
Users, Tenants, Support) · **Money** (Billing) · **Risk** (Spam defence defaults, Trust & safety) ·
**Setup** (System, Feature flags, Providers, Content, IAM). Rejected: keeping the flat strip on
`TabsList`'s horizontal-scroll fallback (that hides overflow rather than organising it), and grouping
by backend module (mirrors implementation boundaries, not the operator's question).

Proven structurally rather than by inspection: `every_admin_tab_is_reachable_through_its_own_group`
walks `AdminTab.entries` by reflection and fails BY NAME if a surface has no reachability marker, so a
regrouping cannot quietly drop a tab; `the_job_group_strip_fits_a_390dp_phone_width_without_scrolling`
measures real rendered node positions; `support_and_trust_safety_stay_hidden_when_their_clients_are_not
_wired` proves the availability gate survived the move.

It also removed a latent bug: the old flat strip appended Support and Trust & Safety at fixed indices
16/17, which would have MISDISPATCHED had only one of the two clients ever been wired. The per-tab
enum identity makes that unrepresentable.

**Bar for the whole slice, non-negotiable:** every control does the real thing against the real backend and
is verified on the rendered client; no read-only panels pretending to be tools; no fabricated numbers; every
destructive or cross-tenant action shows its blast radius before it runs and lands in the audit log.

---

## AT A GLANCE — what is open, in one screen

Read this block first. It is the only summary; everything below is detail.

### S-SPAM — Sery-parity spam defence (owner 2026-09-03: "get the bot to behave like Sery bot")

Spam defence (`spec/spam-defense.md`) is built for L0–L5, the signature store and the dashboard surfaces. Open:

- [ ] **S-SPAM-NETWORK** Signature subscribe/contribute across instances. **BLOCKED, not deferred:**
      it needs a signature service at NoMercy that does not exist, and nothing in this repo can start
      it. The local corpus — store, quarantine, corroboration, curated-skips-quarantine, withdrawal,
      contribution eligibility — is built and is what makes corpus-match and near-duplicate able to
      fire at all.
- [ ] **S-SPAM-FOLLOWBOT-WIRE** follow events → ChannelBaseline → FollowBotTrack → per-account block
  + FollowBotBlock rows. Done-when: a simulated follow spike creates blocks and the dashboard
  restore works.
- [ ] **S-SPAM-LOCKDOWN-WIRE** raid/burst trigger → LockdownWindow → platform chat controls →
  restore on expiry. Done-when: a test hate-raid tightens and later restores the platform rules.
  Also wires JoinBurstFactor (it triggers the lockdown).
- [ ] **S-SPAM-TRUST-WIRE** the live tier resolution (`SpamDefenseService`) gathers watch time and
  calls `AccountRisk.Assess` with the channel's SemiTrustedWatchHoursHere/Instance (today it uses
  hard-coded constants and no caller passes watch time), and the engine checks capability floors via
  `TrustTierLadder.Allows`, with NonLatinScriptGate moving the NonLatinScript floor (today nothing
  calls `Allows`). Done-when: a lurker past the watch-hour setting is Semi-Trusted, and with the gate
  on a newcomer's Cyrillic message is held while a Regular's is not. The spam form marks these three
  settings "not yet active" until then (`SpamSettingCatalogue.PendingSlices`).
- [ ] **S-SPAM-SEED-CORPUS** idempotent seeder that loads `spec/data/spam-seed-corpus.md` as
  SpamSignature rows with Source=Curated. Done-when: a fresh install matches a corpus skeleton.

**Your asks, and where each one is:**

| Your words | Slice | State |
|---|---|---|
| make effects and repercussions visible | S-CONSEQ | law recorded, applies to every slice |
| budget system for payment tiers by resource usage | S-BUDGETS | queued - intent recorded: recover real cost, not upsell |
| old-bot behaviour only from generic blocks | (standing rule) | verified against the spec |
| stream-facing first (commands + overlays) | (ordering) | in force |

**2026-09-24 — the queue is re-based on `usability-inventory-2026-09-24-audit-scope-and-plan.md`
(V·A1–A6, B1–B8, Part C).** Owner: "I thought we were done with the todo's but there is so much
more." Eleven lanes, every "whole platform" claim re-read at the lines, live box checked. Part C of
that doc is the ORDER: Tier 1 runtime (blue/green tug-of-war, Polly noise, scopes required at ENABLE
time) → Tier 2 "silently never worked" (AutoMod key contract, unban-approve, store purchases,
watch-time earning, typed pipeline params, widget settings push, double alerts) → Tier 3 the owner's
flows (impersonation, moderator-of-many, viewer) → Tier 4 form infrastructure incl. the standing
**A6 no-hoops rule (every picker creates inline)** and A5 dead links. Work Part C top to bottom
before anything below this line except the Tier-0 items already here.

**Phase 0-S is EMPTY — closed.** The queue is: the V·Part C tiers above, then Phases 0 and 1 below,
then Phase 2 onward.

**Rules that now bind every future slice** (learned the hard way, each cost rework):
1. A guard that checks only a hand-written list is not a guard — enumerate from the real source.
2. Every model change lands in BOTH migration sets (SQLite AND Postgres) or Postgres deploys break.
3. Never show state that is not actually enforced.
4. Every control says what it does and what changes; destructive saves show a counted blast radius.

---

## BLOCKED ON THE OWNER — cannot be solved from this side

These are not "not done"; they are done-as-far-as-code-can-go and need a real-world action or a call
only Stoney can make. Do not burn agent time trying to work around them.

1. **Discord live-role setup.** The owner's physical steps now live in `DEPLOY.md` ("Discord
   live-role setup checklist").
2. **End-to-end Discord verification is impossible from here.** Unit tests prove the add/remove-role
   call is made with the right arguments against a FAKE handler. They can never prove Discord
   accepted it, that the token is valid, that the hierarchy is right, or that the resolved member is
   really him.
3. 🔒 **S-BUDGETS** classifies *registering a command* as near-free -> abuse floor, not a paid
   ceiling, per his own stated reason (recover real cost, never manufacture upsell). He cited
   commands as an example of a tier limit, so he may want to overrule. Files/TTS/CPU/bandwidth are
   cost-driving either way.
4. 🔒 **S-OWN21** Spotify/YouTube `!sr` needs "message replacement for its og widget body" (owner's
   words). Code tracing found the chain wired end to end (`SongRequestBuiltin` → `MusicService` →
   `SongRequestQueueChangedEvent` → `SrQueueBroadcastHandler` → the `sr_queue` overlay event;
   `now_playing.vue` renders the live track from its own `now_playing` event) and no bug. Done-when:
   the owner names which widget/body was stale (a screenshot of the wrong content) and it is fixed
   or confirmed correct.

---

## OWNER OBSERVATIONS 2026-08-29 — fold in at the right stage, do not jump on them

Live observations from using the bot, given as a batch with the instruction to slot each into
its natural phase rather than working them immediately. Each gets its own slice id (`S-OBS-*`) so they
survive independently as the queue is worked top to bottom.

- **S-OBS-01** stale-then-fresh flash on every page other than Home — a page loads cached data
  first, then the real server response replaces it a moment later, showing wrong data briefly before
  the correct data appears. Done-when: either the cache is never shown when a fresher fetch is
  already in flight, or the UI clearly marks cached data as loading/stale until the real response
  lands — no silent wrong-then-right flash.
- **S-OBS-03** no single place for server errors — errors need to surface both (a) in one consistent
  place (top-of-page banner or snackbar) AND (b) inline at the exact control/location that caused them.
  Done-when: every server error the dashboard receives does both, consistently, everywhere.
- **S-OBS-07** media page's `!media <url>` command works but the resulting media has no click-to-open
  popup, no on-page player, and no overlay-widget playback — it's captured but never actually watchable
  from the dashboard. Done-when: a `!media` result can be opened/played from the dashboard or an overlay.

---

## FUTURE INITIATIVE — global "system widget" architecture (NOT NOW, needs a dedicated co-working session)

Owner, 2026-08-29, explicit: "this is not now, this is when the time is right and the system is stable
to start working on this." Do not dispatch any slice against this until the owner opens that session.
Recorded here so it isn't lost, and so a future session picks the shape up as design context, not a
cold start.

**The shape of the ask:** some widgets are not per-consumer clone-and-customize instances (the current
model — code editor + clone feature per widget) — they are **system widgets**: one global thing the
admin (NoMercy Labs) authors and maintains centrally, and every consumer just picks CONFIGURATION for
it (a/b/c/d-style choices — e.g. corner position: top-left/top-right/bottom-left/bottom-right; a theme
choice) rather than getting their own editable copy. Two named examples:
- **TTS audio widget** — currently presumably has its own widget-clone flow; should instead be a fixed
  system widget whose URL + "open in popup" button live directly on the TTS settings page (no separate
  widget-gallery entry to browse/clone).
- **YouTube widget** — same pattern, same reasoning.
- **Chat overlay widget** — instead of many separate system-provided chat-overlay widget variants
  (one clone per look), this should be ONE system widget with a proper config system (position, theme,
  and whatever else) so a consumer picks from options rather than the admin needing to publish N
  near-duplicate widgets for each visual variant.

**Where this plugs in:** the owner frames this as belonging with the admin side of the code-scripts/
widget-editor system (S-CODE-EDITOR family, closed this session) — an "admin manages/edits the GLOBAL
widgets every consumer uses; the consumer only gets exposed config options" split, as opposed to today's
model where every widget is a per-consumer editable clone. This is a genuine two-tier authoring model
(admin-authored system widgets vs consumer-authored custom widgets) that doesn't exist yet.

**Design continuity:** there's prior Claude Design work on this — a "Chat Widget" design file at
`https://claude.ai/design/p/12517363-1a76-423c-8b05-9ed80f3e353c?file=Chat+Widget.dc.html` — pick that
up as the starting point rather than designing from zero; the owner said it "needs to be improved
first," i.e. it's a draft, not a finished spec.

**How the owner wants this session to run, verbatim intent:** "I want to do a proper co-working session
with you at that point where you ask me the ears off my head like a kid so we can make this awesome."
This means: when this is picked up, load `superpowers:brainstorming` first and interview the owner
thoroughly (scope, the exact config taxonomy per widget type, how admin-authored vs consumer-authored
widgets coexist in the data model, migration path for existing per-consumer widget clones) BEFORE
writing any spec or code — do not jump straight to a design doc or implementation plan.

**Business context**: this would give the owner's designer (aaoa-dev, who makes free stream-overlay
widgets) a real place to contribute polished system widgets that every consumer benefits from, rather
than each consumer needing their own clone-and-customize pass.

---

## Phase 0 — truth and safety

- **S-JWT-HANDLER** replace `JwtSecurityTokenHandler` with `JsonWebTokenHandler` in
  `JwtTokenService`, `ImpersonationSessionService` and `ImpersonationTokenMinter`. Done-when: no
  `JwtSecurityTokenHandler` remains under `server/src`, and the `JwtTokenServiceTests` (tampered,
  expired and wrong-algorithm tokens rejected) and the act-as identity-swap tests pass unchanged.

## Phase 1 — runtime stability

- **S-CONDUIT-LIVE** the dev box runs conduit mode (`EVENTSUB_CONDUITS_ENABLED=true`). Left: (1) a switchover
  with event traffic shows every event exactly once (event journal by EventSub message id); (2) make
  `EventSub:Conduits:Enabled` default-on or remove the flag. Done-when: both hold.
- **S-CMP-A11Y-DIALOG** after any Dialog/Popup closes, the web accessibility tree keeps only the closed
  layer's nodes until a reload (upstream CMP-9368, fixed in compose-multiplatform-core #3298, first shipped
  in 1.13.0-alpha01; we run CMP 1.9.0 on Kotlin 2.2.21). Blocked on CMP 1.13.0 stable. Then upgrade CMP and
  Kotlin (2.3.20+ is required for Wasm since CMP 1.11). Done-when: closing a ConfirmDialog, a Popover and a
  DropdownMenu leaves the page's full a11y tree in `body.shadowRoot`, checked live.

## Phase 2 — existing platforms made to work (Kick / YouTube are shipped features that are broken) — only the spine pieces these fixes REQUIRE

(empty)

## Phase 3 — form infrastructure (stabilizes existing authoring; every 'raw text box' finding rides on it)

- **S043** "All helpers" dialog — wired into every free-text template field that exists: commands,
  event responses, timers, chat triggers, Discord, pipelines, and the rewards `Response` field.
  Giveaways still has no announcement-text field to wire into — front or back — so that half is not
  a wiring task but the separate feature filed as W·§8 i7 (see S065-remaining). Done-when: the
  giveaway announcement field exists and opens the All-helpers dialog.

## Phase 4 — existing features: truth, reach, completeness

- **S060-remaining** Editor fire-bar — desktop (JVM) parity 🔒: the desktop `ProjectEditor` is a
  Swing dialog with no live preview or DOM to fire into, so it needs a product decision on what
  "desktop gets the bar" means before it is buildable (owner call 2026-08-31, not a priority).
  Done-when: the owner names the desktop scope, then the bar fires real per-event samples there.
- **S062** Widget setup — open: inline preview; settings form by schema availability;
  asset/sound/font field types (no backend field type for these exists yet — needs a schema contract
  addition first); editable subscriptions (blocked on S085's `domain.action` naming realignment
  landing first — building this against today's ad-hoc event names would need re-doing). Done-when:
  add → copy → test → live from one row.
- **S065-remaining** announce-mode draw posts nothing and there is no giveaway overlay: add a
  `GiveawayDrawnEvent` consumer and a first-party giveaway widget — Done-when: an announce giveaway
  names the winner in chat and on the overlay. Note: an X winner cannot be DMed a code until S031
  lands an X client with `dm.write`; until then that delivery fails cleanly and the code stays
  `Assigned`.
- **S067** Song-request limits (U·B4) — owner answer recorded: free by default; the max-duration cap
  and the per-user cooldown are always on for everyone; a channel-currency cost is an opt-in
  per-channel setting, default OFF. `MusicConfig` has none of the three fields yet: add
  `MaxTrackDurationSeconds`, `PerUserCooldownSeconds` and `Cost` (entity,
  `MusicConfigDto`/`UpdateMusicConfigDto`, BOTH migration sets, the Song Requests editor), enforce
  them in `MusicService` admission, and charge the requester when `Cost` > 0 (the refund-on-remove
  mechanism already exists). Done-when: every SR toggle changes what `!sr` does (test per setting).
- **S068** Legacy builtins (U·C7) — open: `!songhistory`. No recently-played/song-history read path
  exists in the Music module (`IMusicService.GetQueueAsync` returns only the current track and the
  forward queue), so there is nothing truthful to answer with yet. Done-when: a fresh channel has
  every legacy command or a seed for it.
- **S069** Bot voice everywhere (U·C7, K copy) — open: the per-locale tone catalogue, which waits on
  the 🔒 owner call below (whether the bot ever replies in a non-English language, and what selects
  it). Tone applies only to the bot's own system voice (builtins, `send_message`'s default/fallback
  copy) and never rewrites what a streamer authored (owner call 2026-09-01). Done-when: the bot's
  own system-authored messages sound consistent across surfaces; user-authored content is explicitly
  exempt.
- **S085-remaining** Spec-led contract deltas — open: `IAutomationEventDescriptor` → attribute
  catalog (see S-AUTOMATION-EXPOSE-ALL); `FirstPartyWidgetCatalogue` `domain.action` subscription
  names; the `2026-06-16-database-schema.md` changelog entries; `economy.md` L.3
  `SubjectTwitchUserId`. (`BundleExports` and `CommandFlowSpec` keep integer levels on purpose: they
  are document formats, absent from `openapi/v1.json`.)
- **S-REPLAY-LIVE** click Replay on a real activity row with a real OBS browser source attached —
  Done-when: chat line, TTS and overlay alert all reappear and nothing touches
  currency/loyalty/reward state.
- **S-SR-PUBLIC-PAGE** the public `/sr/{token}` page has no client. The Song Requests screen builds
  that share link, but only the anonymous `PublicSongRequestController` API exists and nothing
  consumes it. Done-when: opening the share URL on the built bundle shows the live queue and lets a
  viewer request a track through the rate-limited public endpoint.
- **S-FAIRQUEUE-ENQUEUE** `FairQueue.EnqueueUnderLock` scans from the end and buries a fresh rank-1
  request; insert front-to-back before the first higher-rank item. Done-when: explicit-order tests
  incl. insert-after-dequeue.
- **S-I18N-NOCHANNELERROR** route the 18 `const val NoChannelError` literals through string
  resources (en+nl). They sit in the Alerts, Analytics, Automation, Bundles, Chat, ChatTriggers,
  Commands, Community, ViewerProfile, Discord, EventResponses, Games, Pipelines, Rewards, Roles,
  TtsQueue, VoiceTriggers and Widgets controllers (Widgets also wraps it as `NoChannelApiError`);
  the OBS and VTS controllers already use resource keys. Done-when: no `NoChannelError` string
  literal remains under `app/`, and a nl-locale test per controller asserts the Dutch string
  (pattern: `ObsControllerLocalizationTest`).
- **S-EGRESS-ALLOWLIST-CRUD** API + dashboard screen to manage `HttpEgressAllowlist` rows (host,
  enabled, methods, body/query/path clamps). Webhooks, custom data sources and sandboxed scripts all
  reject a host with no row, yet nothing lets an owner list, add, edit or remove one. Done-when:
  adding a host in the dashboard lets a webhook to it save, disabling or deleting it makes the same
  save fail, and a counted blast radius shows before delete.
- **S-EGRESS-GUARD-MULTICAST** `EgressAddressGuard` blocks private, loopback, link-local, CGNAT, ULA
  and unspecified ranges but not IPv4 multicast (224.0.0.0/4), the reserved 240.0.0.0/4 block or
  IPv6 multicast (ff00::/8). Done-when: table tests block 224.0.0.1, 239.255.255.250, 240.0.0.1 and
  ff02::1 (plain and IPv4-mapped) while a public address still passes.
- **S-CUSTOMDATA-PUSH** CustomData inbound webhook adapter, or hide the push option until it exists.
  `CustomDataSource.InboundWebhookEndpointId` is defined but nothing ever sets it, so a push source
  cannot receive anything. Done-when: a push source created in the dashboard gets an inbound
  endpoint and a POST to it ingests a `custom.<name>` event (or the option is hidden and tested as
  hidden).
- **S-CHATFILTER-HOLD** Hold = delete + moderation-queue entry, Flag = queue entry only; today
  `ChatFilterExecutionHandler` only logs both. Done-when: a Hold hit deletes the message and
  enqueues a pending `ModerationQueueItem`, a Flag hit enqueues without deleting, and the queue
  approve/deny works on both.
- **S-CHATFILTER-ENUM-WIRE** `ChatFilterDto.FilterType` and `.Action` serialize as raw ordinals
  because no `JsonStringEnumConverter` is registered; the dashboard only works by round-tripping
  opaque values. Done-when: GET returns the enum names as strings, and the Kotlin client,
  `openapi/v1.json` and `ApiContractTest` agree.
- **S-CHAT-SETTINGS-FIELDS** `ChatSettings` in `ChatApi.kt` has no `uniqueChatMode` (R9K),
  `nonModeratorChatDelay` or `nonModeratorChatDelayDuration`, which the backend `ChatSettingsDto`
  sends to Twitch. The whole-object PUT therefore resets them to off on every save. Done-when: the
  Chat screen toggles all three, and a test asserts that saving one setting round-trips the others
  unchanged.
- **S-MOD-PIPELINE-ACTIONS** add `warn`, `add_chat_filter_hit` and `apply_heat` pipeline actions,
  and route the Ban/Timeout actions through `IModerationService` (today `BanAction` and
  `TimeoutAction` call `IChatProvider` directly, so no mod-log row, escalation or audit is written).
  Done-when: the three actions are registered and appear in the block palette, and a pipeline
  Ban/Timeout writes the same mod-log row as the dashboard action.
- **S-FUNPRESET-BRACES** `FunCommandPresets` use `{{user.name}}` / `{{args.1}}`; verify what the
  single-brace resolver renders and fix the presets. Done-when: a test resolves every seeded
  fun-command preset through the real template resolver and no literal braces reach chat.
- **S-DASH-JAR-STALE** savings-jar numbers on the dashboard go stale. `JarContributedEvent`,
  `JarWithdrawnEvent` and the goal/membership events reach no hub push, and
  `EconomyController.subscribeToHub` reloads only on the `catalog` config change. Done-when: a
  contribution from a viewer or another member channel moves the open jar balance without a reload
  (hub push test + controller test).
- **S-SCHEDULE-TZ** the Schedule screen prints raw ISO strings (`startTime → endTime`), the segment
  read carries no timezone so the edit dialog cannot prefill it, and a new segment's timezone is a
  typed IANA string. Done-when: rows show start/end in the streamer's saved timezone, the timezone
  is picked from a zone list that defaults to it (S107's picker rides on this), the edit dialog
  seeds it, and an invalid zone cannot be submitted.
- **S-PINGDISCORD** `!pingdiscord` builtin, identity-link-gated (decided in
  `2026-06-16-discord-notifications.md`): it resolves the chatter's linked Discord identity
  (`UserIdentity`, provider `discord`) and toggles their opt-in for the channel's notify role; a
  chatter with no linked Discord identity gets a reply pointing at the link flow. Done-when: with a
  linked identity the command opts the member in and out (opt-in row and role call asserted),
  without one it replies with the link hint and changes nothing.
- **S-WALLET-UNIQUE** `CurrencyAccount` has only a plain index on `(BroadcasterId, ViewerUserId)`;
  "one wallet per viewer per channel" rests on the service's lazy create, so a race can mint two.
  Done-when: the index is UNIQUE in BOTH migration sets, the migration first merges existing
  duplicate wallets (balances and lifetime totals summed into the oldest row, ledger references
  repointed) and is proven on a populated database, and a concurrent-create test yields one wallet.
- **S-AGECONSENT-NULLABLE** `ViewerAgeConsent.ConsentRecordId` is a non-nullable `Guid`, so
  `AgeConsentService` writes `Guid.Empty` as a sentinel for an inferred adult (no consent record
  backs an inference; economy K.8 says `Null`). Done-when: the column is `Guid?` in BOTH migration
  sets (existing `Guid.Empty` rows become null), no `Guid.Empty` sentinel remains, and the
  inferred-consent tests assert null.
- **S-KEK-LINUX-MAC** `OsSecureStoreKeyVault` custodies the root KEK in the OS store only on Windows
  (DPAPI); macOS Keychain and Linux libsecret are still missing, so non-Windows self-host derives
  the KEK from `Encryption:Key` via HKDF. Done-when: on macOS and on Linux with a keystore the KEK
  is generated once and persisted in Keychain / libsecret (no plaintext key file), the
  encrypted-file/env fallback stays only for headless hosts, and each backend has a round-trip test.
- **S-AUTOMATION-EXPOSE-ALL** `AutomationEventRegistry` builds the public event catalog only from
  hand-written `IAutomationEventDescriptor`s, but `spec/automation-api.md` D6 exposes every
  `DomainEventBase` subtype under its wire name from attributes, with a PII projection. Done-when:
  an event type with no descriptor reaches a subscribed automation session under its default wire
  name, `[NotExposed]` members never leave the server, and `[Pii]` members are stripped for Public
  sessions (tests).
- **S-WATCHLIST-PERSIST** the multi-chat watch list is not persisted: `MultiChatController` takes a
  `WatchListStore` that defaults to `NoOp`, and no real store is wired in the app graph. Done-when:
  the watched channel set survives an app restart (an expect/actual store wired in `AppGraph`), with
  a test that a second controller instance restores it.

## Phase 5 — new model (D1 one channel / D2 any login) — merged only after Phases 0–4; S023/S024 are the minimum the viewer-identity fixes need

- **S019-remaining** `PlatformConnection` model — `Channel` loses `Provider`; attaching a SECOND
  platform to an existing channel; data migration folds existing sibling channels into one (U·C0,
  spec `platform-identity.md`). Done-when: a Twitch+Kick streamer is ONE `Channel` with two
  connections; all tenant-scoped reads unchanged.
- **S023-remaining** Viewer identity key sweep — open: `BuiltinCommandContext` carries no
  `Provider`, so `PermitBuiltins` and `AccountAgeBuiltin` default to Twitch (plumb it through every
  builtin dispatch); `ChannelsController.EnterModeratedChannel` is genuinely Twitch-only today
  (Helix Get Moderated Channels has no Kick/YouTube equivalent yet); `*TwitchUserId` →
  `*ExternalUserId + *Provider` on the 18 entities; delete `PlatformType` (U·C0). Done-when: build +
  migration green; no call site defaults the provider.
- **S024** Viewer linking — `LinkAsync` absorption (§3.1a), `IViewerMergeParticipant` + the eight
  participants, `ViewerRowAbsorbedEvent` published (U·C0). Done-when: a viewer who chatted on Kick then
  links Twitch ends with ONE User row and ONE balance (test).
- **S025** Login any platform (D2) — `auth/providers`, `auth/{provider}/device`, `/poll`; first
  login creates the channel, others attach (spec `identity-auth.md`). Done-when: a Kick-only
  streamer signs in, onboards, and has a channel with one Kick connection.
- **S026** Onboarding "connect more platforms" stage (non-blocking) + channel-bot connect via device
  code with poll/refresh (U·B6, spec `onboarding-setup.md`). Done-when: wizard attaches a second
  platform; bot-account card updates without reload.
- **S032** Combined management fan-in/out — combined chat composer with target selector + per-target
  result; per-line platform badge incl. Twitch; render 7TV zero-width emote stacking; `provider` on
  `ChannelSummary`; (provider,id) dedupe + reorder window; timers/event responses/announcements
  platform target sets with per-platform rate limit + duplicate suppression; one go-live form with
  per-platform results; per-platform viewer breakdown + total + cross-platform stream session;
  owner-scoped "ban on all my platforms"; earning credits the linked person (U·C1). Done-when: live
  on three platforms, one timer posts once on each; one ban bans the human everywhere; Home shows
  per-platform viewers + total.
- **S-VIEWER-RESTRICTION-D1** `ChannelModerationStanding` is per platform (`Provider` + raw `UserId`
  + `Standing`), so a Twitch mute does not mute the same human's Kick identity. `moderation.md` J.12
  (D1) is `ChannelViewerRestriction` keyed on `Users.Id` with
  `Restriction {muted|shadowbanned|blacklisted}`. Rename and rekey it, resolve the inbound identity
  through `IUserIdentityService.ResolveUserAsync` at the enforcement seams, and migrate existing
  rows in BOTH migration sets. Done-when: a mute set on a viewer's Twitch identity also ignores the
  same human's linked Kick identity, and a blacklist drops their chat at all three publishers.
- **S-COMMUNITY-MULTIPLATFORM** `CommunityController` is Twitch-only: the followers, VIP, subscriber
  and moderator tabs come from Helix, and viewer lists and pick options key on `User.TwitchUserId`,
  so a Kick- or YouTube-only viewer never appears and cannot be acted on. Done-when: the Community
  lists key on the person (`Users.Id`) and show each viewer's platform identities; a Kick-only
  chatter appears and can be looked up and moderated; platform-only tabs name their platform and
  read only from an API that really exposes them (never fabricated).

## Phase 6 — new features and personas (D3 X, D4 viewer, moderator-of-many, new capabilities)

- **S044** Helper expansion + presets — math/string/date namespaces, general `{{ns.key:arg}}` grammar,
  any-step output, `{{stream.viewers}}`; raid preset (shoutout → raid → countdown → optional OBS/
  Spotify) + `channel.raid.out` seeded on onboarding (W·§6, U·A1 i4). Done-when: `!raid <user>` from a
  fresh channel runs every step or names the failing one.
- **S054** TTS segments — `TtsSegment` list request, per-segment voice mode, ONE `tts_speak` payload
  with ordered segments; `BypassQueue`; sub-streak preset (U·A5, spec `tts.md` §6). Done-when: the
  owner's example plays as one utterance with two voices.
- **S056** Discord triggers + action — `go_offline` + `hype_train` with handlers; action carries own
  channel/template/embed/ping; Event Responses Discord preset (U·A6 i2/3/6).
- **S057** Discord live-role sync — REST endpoints to create/update/delete `DiscordLiveRoleConfig`
  plus the dashboard role picker and per-guild config UI (U·A6 i4). Done-when: go live → roles on;
  offline → roles off.
- **S059** Alert system surface — moderation retraction (`widgets-overlays.md` §2a) still has to be
  honoured on every surface; the domain event and hub plumbing are built. One slice each:
  - **S-RETRACT-b** Chat + Alert surfaces honour it (remove the message node / cancel an on-screen or
    queued `AlertQueueEntry` — this is where the original "queued-but-undelivered" note lives).
  - **S-RETRACT-c** TTS surface (`tts.md` §3.4a — stop mid-sentence, drop matching queued utterances).
  - **S-RETRACT-d** Sound clip + custom-widget SDK surfaces (`widget-sdk.md` — the SDK's chat/alert
    helpers retract automatically; a bespoke widget reading raw events must retain `SourceMessageId`
    to correlate).
- **S071** Notification centre + Home (U·B6, K) — open: the action-required inbox covers only dead
  integration tokens and held AutoMod messages. Missing scopes (every gap is progressive by design),
  failed timers (`Timer` has no run-failure tracking) and pending unbans (only live-fetchable under
  an operator token) have no honest backing signal yet. Done-when: timers record run failures and
  the inbox lists them, and each remaining category is either backed by a real signal or documented
  as not applicable.
- **S072** IA reconciliation (U·B6) — open: `MyData` is still a Setup page with a Moderator floor
  (it belongs on the participant rung, see S077); Settings is one scrolling column, not tabbed; the
  Commands manage floor is Moderator; `frontend-ia.md` drifts from the shipped nav for Event
  Responses/Alerts placement. 🔒 Admin via profile menu + chrome swap: `Beheer` renders as its own
  standalone sidebar link under CONFIGURATIE, not folded into the profile-menu dropdown alongside
  theme/Account as first specced — confirm the profile-menu destination is still the intended target
  before building it (the sidebar link may have been a deliberate later call). Done-when: `MyData`
  opens for a role-less viewer, Settings is tabbed, and `frontend-ia.md` matches the shipped nav.
- **S031** X Live as a platform connection (D3) — `IntegrationProvider.twitter`,
  `AuthEnums.Platform` gains `x` (D3), login + connection, chat read/send via X's API to the extent
  it exposes (document limits), events where available (U·C4, spec `platform-identity.md` §10).
  Done-when: X connection attaches; chat lines carry `x`.
- **S073** Moderated-channel discovery per platform; reconcile covers moderator-mode tenants +
  "roles last synced"; live dot bound to `isLive`; roster refresh on `StreamStatusChanged`; roster
  cached (U·C6).
- **S074** Never act on the wrong channel — stale active-channel pin detected + cleared + explained;
  `primaryChannel()` hard-fails instead of substituting; switch splash with timeout/error; active role
  in the sidebar header (U·C6). Done-when: revoked access yields an explained state, never a 403 loop.
- **S075** Cross-channel awareness — hub joins every roster channel for alert/mod classes; attributed
  notifications with click-through; `GET /me/moderation/queue` + "my channels" home; queues re-fetch
  on `ModAction` (U·C6). Done-when: a mod of 4 channels sees which is live and gets attributed alerts.
- **S077** Viewer entry — switcher source "channels I appear in"; honest empty state; `MyData` floor
  to Everyone on the participant rung (D4); channel chip shows the channel; routes/deep links
  (U·C5). Done-when: a role-less viewer's first run lands on a usable Me page.
- **S078** Me page — GDPR export/erase, linked platforms (identity API client), own TTS voice, standing,
  profile fields, leaderboard opt-in read, per-jar contributions, own SR requests + public page link,
  preview-as-viewer forces Everyone (U·C5).
- **S079** Viewer giveaway entry/my-entries endpoint + card (or drop from IA) (U·C5).
- **S-BOT-IMPORT** One-click import from other chat bots (owner 2026-09-30: "a one-click import from
  streamelements and the others for their enabled commands and quotes"). One importer per service
  (StreamElements, Nightbot, Streamlabs Cloudbot, Moobot, Fossabot, Wizebot, Streamer.bot — each
  service's export path (API, OAuth scope or file) researched and recorded first); enabled commands
  land as custom commands with their variables mapped to our helpers (an unmappable variable is
  named, never dropped silently); quotes land in Quotes with author and date kept. A preview lists
  what will be created, skipped and renamed before anything is written. Done-when: from a live
  StreamElements channel, one click imports every enabled command and every quote, and a re-run
  creates no duplicates. Research 2026-09-30 (verify each against a live channel before building):
  StreamElements = API, `bot:read` OAuth2 or JWT, commands `GET /kappa/v2/bot/commands/:channel`,
  quotes route unofficial; Nightbot = API `GET /1/commands`, OAuth2 scope `commands`, no quotes system;
  Fossabot = unauthenticated public-commands endpoint (unverified); Moobot and Streamer.bot = export
  file only; Streamlabs Cloudbot and Wizebot = no public read API found. An `Import` module with a
  StreamElements export DTO already exists (`Application/Import`, `ProviderImportService`) — extend it.
- **S-CONTRIB-GUIDE** Owed only the proof: file one issue through each form (bug, feature, platform
  API gap) as a person and one as a bot (`gh issue create` with the form headings and the yaml block), then
  close them. Done-when: an agent reproduces each defect from the issue alone, without asking a question.

## Phase 4B — the surfaces round four found (U·Part E) — existing features, same stability-first rule

- **S100-remaining** Custom data sources truth — drop or wire `InboundWebhookEndpointId` (U·E3);
  tracked as S-CUSTOMDATA-PUSH in Phase 4.
- **S101** Supporters — provider list + capabilities from the backend (`GET /supporters/sources`),
  mode-correct connect forms (secret / socket token / OAuth connection), error state + reason, staleness-
  derived status, per-connection test; resolve `SupporterUserId` where payloads allow + amount-scaled
  earning; dedup unique-violation handled; event-type in Patreon/Treatstream dedup key; source filter;
  ingest failure counter surfaced (U·E4). Done-when: all 11 adapters connectable and truthful.
- **S102** Billing/usage truth — Usage panel reads real counts for count-capped keys, localized labels,
  unlimited rendered; `UsageQuotaExceededEvent` + `SubscriptionTierChangedEvent` consumers; `free` tier
  limits seeded; downgrade at-period-end + over-cap warning (U·E4).
- **S103** Bundles + pick lists — export all 12 types; type filter select; semver + tags chips; installed
  version compare/update; pick-list anti-repeat window, per-item weight/enable, ETag, bulk paste/import/
  reorder (U·E4).
- **S104** Media share + sound + assets — media player widget (system surface) consuming `GetNext`;
  moderation rows with thumbnail/link/name; submitted/playback events as trigger sources; paged queue;
  sound handle threaded end-to-end; upload dialog with volume/trigger/cooldown/floor; clip replace;
  asset picker wherever a media URL is configured; used-by guard; limits shown; paging (U·E2).
- **S105** OBS + VTS truth — OBS page consumes OBS events; scene/source/input pickers in pipeline
  fields; source-visibility + replay-buffer on the control screen; bridge error vs offline;
  edit-reset fix; VTS probe + bridge status; inventory failure vs locked; parameter/tint control;
  endpoint prefilled + validated (U·E2).
- **S106** Stream / live-ops page — dedicated Stream destination (stream info incl. language per
  platform connection, polls/predictions with live results + hub refresh, ad countdown + snooze, raids,
  markers, clips, shield, hype train, goals, charity, guest star); errors not swallowed; raid-pending
  cleared; platform badge on every control (U·E1). Done-when: an operator runs a poll and sees votes
  move without reload.
- **S107** Schedule + journal — pickers for start/timezone/duration, edit seeds timezone, formatted rows,
  webcal subscribe URL surfaced; journal list/query endpoint + browse/filter/inspect UI; rebuild status
  polling; replay/import-legacy reachable or removed (U·E1).
- **S108** Analytics truth — failures visible, selectable window + metric, local-day boundary,
  platform-analytics client (U·E1).
- **S109** Code scripts developer experience — capability catalogue + per-script declared/granted/denied
  view with links to the toggles; all failing capabilities reported at save; SDK types failure visible;
  starter templates + capability chooser; used-by view; test-run with triggering user; execution history;
  bridge unwired capability throws; desktop editor parity decision stated in-app (U·E3).
- **S110** Automation + federation — Stream Deck run-pipeline/run-command action with picker; federation
  opt-in validated server-side, peer/capability pickers, Direction collected (U·E3).
- **S112** Self-host ops — version stamping (`/health/version` real); ready = migrations + EventSub,
  Degraded ≠ ready; update check + notice; pre-migration DB snapshot + documented rollback;
  backup/restore verb in deploy scripts; versioned image tags; firewall + log path documented, log
  size cap; tray parity on Linux/macOS or printed URL/PID; `.env` dev-password warning at boot;
  boot-time notice in saas mode (U·E5).
- **S113** Quality gates — in-process E2E host fixture so the suite runs by default; typed
  `ProducesResponseType<T>` on the 157 schema-less operations + a regenerate-and-diff contract test; Esc
  in the shared dialog; label the 17 icons; move the 47 literal labels to strings.xml; locale date/number
  formatter; hub reconnect jitter; `primaryChannel()` cached (S050 dependency); Wasm optimize step with a
  size budget; chat decoration + pronouns + engagement get a settings surface (U·E6, E4).

## Phase 6A — platform admin: reliable system-level management (U·Part D) — safety items first, then reach

- **S087** IAM mutation audit + guards — every assign/revoke/create/deactivate/reactivate writes an
  `IamAuditLog` row with target/role/scope; create transactional + validate-before-mutate; no duplicate
  or inactive-target assignment; last `iam:manage` holder protected; flag changes audited (U·D2).
- **S090** Support access that works — session grants scoped read-only Plane-B visibility (RoleResolver
  reads the session); list active grants; any `iam:manage` holder can end any grant; expiry reaper;
  "view as tenant" reuses preview-as-viewer (client downgrade) (U·D4). Done-when: support staff can read
  a tenant's console without impersonating.
- **S091** Platform-wide user controls — user detail endpoint (channels, identities, sessions,
  consent); platform disable/ban; `MergeIdentitiesAsync` exposed; compliance key for admin erasure
  (not `tenant:access`) (U·D3).
- **S092** Tenant ops — delete/purge + ownership transfer (writes `DeletionAuditLog`); per-tenant billing
  state + quota/limit view; re-run seeds for a tenant; rotate tenant tokens/secrets; `IgnoreQueryFilters`
  on admin lists; search by id/owner/GUID; Sort/Order honoured; real stats (no hardcoded "healthy"/0);
  rate limits + explicit target confirmation on destructive admin ops (U·D3).
- **S093** Ops visibility — EventSub session inventory per broadcaster; token health across tenants;
  worker status + queue depths; error-log surface; AdminHub connect snapshot + scoped pushes;
  break-glass/denial alerts from `IamAccessEvaluatedEvent` (U·D2/D3).
- **S094** Billing roles — `billing:write`/`billing:grant` keys on the four billing writes; `billing:refund`
  endpoint or key removed; `platform-billing` role usable (U·D2).
- **S095** Admin UI truth — render `state.error` + every slice's failure; writes route through
  `actionError`; paging on every list; refresh per tab; hub live state truthful + connect errors
  surfaced; Admin entry in profile menu gated on Plane-C roles with chrome swap + per-page
  routes/deep links (U·D6). Done-when: a 403 on any admin write is visible.
- **S096** Admin UI reach — flag editor (enable/rollout/tier/mode + per-tenant overrides); invite dialog
  (count/tier/expiry/founder); grant tier/founder actions; support-access begin/end + active list;
  impersonate confirm + justification; reasons + confirm on revoke/deactivate; Ban escalated behind
  name-echo confirm; audit filters as pickers + date range; role keys viewable + role CRUD; Channels tab
  merged into Tenants; timestamps formatted; one primary action per admin page (U·D6, K).
- **S097** System-level content — `SystemPreset` (kind command|pipeline|event-response|pick-list|tone|
  announcement, key, payload, version, enabled, origin seeded|operator) seeded from today's static
  catalogues; `/admin/presets` CRUD + `SystemPresetAdoption` (auto|optin|declined, version) with push-to-
  all and per-tenant opt-in; `Widget.IsSystem` + delete protection + restore; catalogue version stamp on
  gallery items + installed widgets ("update available"); admin kill-switch for a first-party widget and
  a builtin (`BuiltinCommandRegistry`); `PlatformNotice` (announcement/maintenance banner) read on
  bootstrap (U·D5). Done-when: the operator creates a custom command preset and every opted-in tenant gets
  it without a redeploy.

## Phase 7 — polish and structure

- **S080** Sleak pass (K) — toggles neutral + one accented CTA per screen; chat-colour clamp; accent
  derivation floor; form width cap; random-responses segmented control; chat-mode on/off state;
  concentric radius tokens; 13 px muted contrast; one identity block; re-render the six screens +
  Overlays/Economy/TTS/Integrations/Pipelines and re-run the checklist.
- **S081** Widget component splits (W·§7/§8 i11) after S058; `WidgetGalleryItem` file-set storage first.
- **S082** Drop game redesign 🔒 mechanic; stacked-transition chat style 🔒 reference (W·§8 i5/i8).
- **S083** Render-manifest + per-page hub event-class subscriptions.
- **S084** Remaining per-widget nits (W·§8 i10), the 15 code scripts test-run on the live channel, S
  LOW/informational list.
- **S-GLYPHBUTTON-A11Y** found by S047-remaining (39314dd5): `GlyphButton.kt`'s `clearAndSetSemantics`
  wipes ancestor-contributed Disabled/stateDescription from its own semantics node, so a disabled
  `GlyphButton` only exposes its disabled state via a wrapping node (e.g. `ManageGate`'s `Box`) — affects
  every disabled icon-button app-wide, not just the pipelines Test action. Done-when: a disabled
  `GlyphButton` reports Disabled/stateDescription on its OWN semantics node (assistive tech reads it
  without depending on a specific wrapper), proven by a jvmTest on the component directly.
- **S-AUDIT-REMAINDER** Interactive-surface classes still open (moved from the retired 2026-07-20 audit
  ledgers). Re-verify each against the live client before fixing; drop any that no longer reproduce.
  (1) **C5 stale-after-write:** Discord roles card (`DiscordScreen.kt`, roles keyed on
  `connection.id`, role add/edit/delete/post never re-fetch); Economy `JarManageDialog` memberships
  (`LaunchedEffect(jar.id)`, invite/accept/remove reload the page, not the dialog; a `getJar` failure
  reads as "no memberships"). (2) **C1 can't-unbind:** Timers pipeline, ChatTriggers pipeline→text and
  Rewards limits/colour cannot be cleared (`explicitNulls=false` drops the null) — needs an explicit
  clear flag on the update DTOs. (3) **C6 empty-state:** Roles list still shows Empty instead of
  Ready. (4) **C4 capability without UI:** Admin FeatureFlags toggle, grantTier/grantFounder/
  beginTenantAccess, federation peer keys, settings validateInvite, schedule cancelSegment (one
  occurrence), games revokeConsent, chat slowModeDelay/followersOnlyDuration inputs. (5) **C9 pickers:**
  Economy jar contribute/withdraw/invite and Participant Points/Store transfer take raw ids — use
  `SearchPickerField`. (6) Single defects: `ScheduleScreen` `SegmentDialog` edit seeds timezone/
  duration blank while `canSave` requires them (title-only edit impossible); Economy `AccountsSection`
  reads the first page only (`EconomyApi.accounts`), so accounts past the first page are unreachable;
  analytics opt-out failure feedback;
  `ChatController.ban` partial-failure surface; `AnimatedImageDecode` Skia bitmap leak (`.close()` per
  frame); file-picker `<input>` leak; CustomEvents field-map serialization; `run_code` `code_script_id`
  picker; drop the unsupported "svelte" framework option. Done-when: each fix has a consequence test
  (state change visible after the write) and a rendered-client check.
- **S-READINESS-REMAINDER** Configuration gaps left after the 2026-07-19 readiness sweep: music
  playback-volume slider on the Music screen (`song_volume` action and `SetVolumeAsync` exist); direct
  VTube Studio model-move control (`vts_move_model` action exists, no screen control); Assets
  tags/folders; a click-through of economy, webhooks, sound and OBS-mixer screens on the rendered
  client; Helix `schedule/icalendar` and `clips/downloads` coverage checks (full external-API coverage
  rule — verify against live docs first).
- **S-APPTESTS-FLAKE** `NomNomzBot.Application.Tests` fails about 1 run in 20 and the failing test is
  not named. Done-when: the next red run's test name is captured and that test is fixed so it fails
  only for the right reason.
- **S-COVERAGE-REMAINDER** External-API coverage gaps found by the retired requirement-test pass
  (the `Requirements/` suites are not in the tree). Verify each against the live API docs and current
  code, then implement (full-coverage rule: a gap is added, never dropped): YouTube
  `liveChatModerators` (no code hit today); Discord webhook management (no code hit today) and the
  remaining Discord guild surface (edit/delete message, list members, create channel) beyond
  `IDiscordBotGateway`; Discord OAuth moved into the descriptor-driven `IOAuthProviderRegistry`
  instead of the bespoke `DiscordOAuthController`; Twitch Helix analytics/conduit/extensions/drops
  are org-gated or opt-in and stay under the ROADMAP owner-confirm list. Done-when: each capability
  has a service method, a route, and a test that asserts the outbound request shape.
- **S-LEADMOD-APP-RENAME** 🔒 pending owner answer on the LeadModerator display name. Rename Kotlin
  `ManagementRole.SuperMod` to `LeadModerator` plus its string keys (the server already says
  LeadModerator). Done-when: no `SuperMod` identifier or string key remains under `app/`, and
  `ShellNavTest` and the manage-gate tests pass.
- **S-RETIRE-LEGACY** delete `PermissionsController` + `IPermissionService` + `PermissionService`,
  the unused `commands:builtin:*` seed keys in `ActionDefinitionSeeder`, and the legacy
  `ChannelSubscription` entity (both migration sets). Done-when: a full caller trace (DI
  registrations, reflection scans, routes, openapi, app client) shows no caller before deletion, and
  build, both migration checks and `ApiContractTest` are green after it.

## 🔒 Owner calls still open
- SignalR/Redis backplane for multi-replica (S035) — single-instance acceptable for now?
- Cooldown DB write-through (S040) — scaling investment, defer?
- Sidebar regroup vs `frontend-ia.md` update (S072).
- Per-locale tone catalogue and bot-reply language (S069) — does the bot ever reply in a non-English
  language, and what selects it? `Channel.Language` is saved but read nowhere; D9 needs a decision.
- Drop game mechanic; stacked chat transition reference (S082).
- Pre-existing from BUILD-TODO: authz key names (Plane-C + Gate-2), self-host owner = platform admin,
  user-scripting model (JS-first), YouTube non-BYOC client, Stripe, pipelines 6-surface unification,
  community reposition, data-sources push-bridge, federation transport, Streamer.bot import.
