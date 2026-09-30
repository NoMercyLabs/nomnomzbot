# Owner questions from the 2026-09-30 documentation realignment

The realignment workflow (82 agents, every doc, rule, skill, script and memory file checked against the code) fixed 668 verified factual problems. These questions were held back because each one is a product or policy choice, not a fact. Each has the options and a recommendation. Answer by number; answered questions get deleted from this file.

## 1. Sleak rule wording in CLAUDE.md

CLAUDE.md says Sleak's 'three core rules' are one primary action per group, concentric radius and scarce accent. The vendored Sleak SKILL.md lists its core rules as concentric radius, section eyebrows (max one per page) and intentional/scarce accent. The audit wanted to rewrite the bullet and to add a new policy: role-gated actions stay disabled-with-reason, and Sleak's avoid-disabled advice applies to form validation only. The same text was proposed for scoped-rules design-system-shadcn, dispatch-a-builder item 4 and build-app house rules.

**Options:**
- Keep your three rules as project rules, and add section eyebrows as Sleak's third core rule. Attribute 'one primary action per group' to the project, derived from Sleak.
- Keep the bullet exactly as written.
- Adopt the full rewrite, including the disabled-with-reason policy.

**Recommendation:** The first option: attribute rules correctly, add eyebrows, keep your three rules. Decide the disabled-button policy separately.

## 2. NoMercyBot rule vs legacy-import references

CLAUDE.md says 'there is no NoMercyBot left in code or specs'. The code legitimately references %LOCALAPPDATA%\NoMercyBot (DefaultLegacyDatabaseLocator, the legacy import source). Comments cite legacy NoMercyBot.* class names, and NoMercyBot.Api.http is a dead template (removed in this plan).

**Options:**
- Add a carve-out: references to the OLD bot as an import source or history are allowed and are not a reintroduction.
- Keep the rule strict and rename or strip the comments (the import path must stay, since it is the old bot's real folder).

**Recommendation:** Add the carve-out. The import path cannot change, and the rule is about namespaces, assemblies and projects.

## 3. aitm vs Grimora

aitm-setup.md is a HARD memory: 'aitm is mandatory; run it FIRST in every session', and C:/Projects/aitm/bin-cli still exists. The audit claimed aitm is retired and wanted to rewrite the aitm references to Grimora in CLAUDE-adjacent docs, spec-editor.md, EXECUTION-PROMPT, ROADMAP, implementation-workflow and several memories. CLAUDE.md already says Grimora indexes the reference docs, and Grimora exposes brain_stage/brain_flush tools.

**Options:**
- aitm is retired and Grimora replaces it: rewrite the HARD memory and all references.
- Both stay: aitm CLI for mem/recall, Grimora for docs.
- aitm stays mandatory; leave the references.

**Recommendation:** Confirm which one you actually use now. If Grimora replaced aitm, the HARD memory must be rewritten first, because today it tells every session to run a tool the docs call retired.

## 4. Track & Team section in CLAUDE.md

The audit wanted to cut the pre-campaign Team & Track section and the Handoff subsection down to a 3-line stub, and to shorten the Critical Rules bullet. CLAUDE.md says the section 'stays on the books below and resumes when the campaign ends'. This plan only annotates Git Conventions line 161 and the scoped-rules entries as suspended.

**Options:**
- Keep the full section as written (it resumes after the campaign).
- Move the pre-campaign text to a separate doc and leave a pointer.

**Recommendation:** Keep it. It is your explicit resume-later rule, and the D7 annotation already stops it from misleading agents.

## 5. CI and local test policy

CLAUDE.md CI Gate: run the full suite before EVERY commit and block on `gh run watch --exit-status` after EVERY push. The frontend clause names only jvmTest. Memory (HARD, 2026-09-24/26) says: CI/deploy run in the background and only warn; push in milestone batches; use the watch-ci skill; run only the touched test projects locally; also run compileKotlinWasmJs. EXECUTION-PROMPT line 13 says 'NO pushes, NO CI'.

**Options:**
- A: Newest memory wins. Per commit run slice-check; run verify-tree at checkpoints; push validated batches in the background via push-and-watch.ps1; fix red immediately; add compileKotlinWasmJs to the frontend gate. Rewrite CLAUDE.md CI Gate, watch-ci, build-server, scoped-rules and EXECUTION-PROMPT to match.
- B: Keep CLAUDE.md as written and delete the conflicting memories.
- C: Hybrid: background watch, full suite before every push.

**Recommendation:** A. It is your latest instruction and matches the scripts. Master still never stays red.

## 6. Commit sign-off (-s)

Memory always-sign-off-commits (HARD) and commit-a-slice say every commit uses -s. Recent commits are unsigned. The prepare-commit-msg net is bypassed by core.hooksPath=.githooks.

**Options:**
- Keep the rule and enforce it with a hook in .githooks/, plus a CLAUDE.md line.
- Drop the rule and delete the memory.

**Recommendation:** Keep and enforce it via .githooks/.

## 7. License header on EF migrations

CLAUDE.md requires the header on every source file. Several hundred generated migration, Designer and snapshot files carry only // <auto-generated />.

**Options:**
- Exempt generated migrations (IDE0073 none for the Migrations folders).
- Require it and backfill.

**Recommendation:** Exempt them.

## 8. Portable hooks and tracked settings.local.json

scoped-rules checks use absolute owner-machine paths (C:/Projects/NoMercy/.claude/scripts, the main-checkout RULES_REGISTRY/RULES_CLAUDE_MD). A gate run from a worktree therefore checks the main checkout. .claude/settings.local.json is tracked.

**Options:**
- Make them portable ($CLAUDE_PROJECT_DIR-relative), untrack settings.local.json, ship .claude/settings.json.
- Keep them machine-local.

**Recommendation:** Make them portable.

## 9. PRODUCT-ALIGNMENT D3 wording

D3 says 'Posting tweets/clips is a later announcement-target feature, not now', which is deferred framing that D9 forbids. The audit wanted: 'not in scope; recorded in the tracker as an idea (D8)'. D3 is your decision text in the binding file.

**Options:**
- Reword as 'not in scope; tracker idea'.
- Reword as a decided slice.
- Leave as is.

**Recommendation:** Reword to 'not in scope; tracker idea'. It keeps D3's meaning and satisfies D9.

## 10. LeadModerator display name, glossary and app rename

Code renamed SuperMod to LeadModerator (f5c079052). The Kotlin enum and strings still say SuperMod / 'Super Mod'. The audit wanted a glossary row in PRODUCT-ALIGNMENT (LeadModerator, display 'Lead Moderator', ladder Moderator < LeadModerator < Editor < Broadcaster) and the app rename slice S-LEADMOD-APP-RENAME.

**Options:**
- Confirm 'Lead Moderator' everywhere: add the glossary row and run the app rename.
- Keep 'Super Mod' as the user-facing label.

**Recommendation:** Confirm 'Lead Moderator'.

## 11. Is the database schema LOCKED or extendable

2026-06-16-database-schema.md says 'STATUS: LOCKED' and spec-editor enforces changelog lines. Memory (HARD) says 'schema not locked, extend freely'.

**Options:**
- Extendable: 'design baseline; code is truth for built tables'.
- Locked.

**Recommendation:** Extendable, with the new Implementation status table.

## 12. Data-access rule: repositories vs IApplicationDbContext

Five docs say 'Repository + IUnitOfWork, no raw DbContext'. Hundreds of Infrastructure files and 16 controllers inject IApplicationDbContext.

**Options:**
- Bless reality for services; one slice for the controllers.
- Keep the strict rule and add a repository layer.

**Recommendation:** Bless reality for services and add one controller slice.

## 13. docs/bot-capabilities.md: retire or maintain

A third capability inventory with a stale 'not built' list, stale hub events and the retired SuperMod term. The fix-up edits were deferred to this answer.

**Options:**
- Retire it (git rm; point at FEATURES.md).
- Maintain it and apply the fix-up edits (drop counts, delete section 14, update the hub table, LeadModerator, pipeline counts).

**Recommendation:** Retire it.

## 14. Channel model in schema and code (D1)

D1 is one Channel with many PlatformConnections. Code keeps Channel.Provider/ExternalChannelId, and a Channel.cs comment says other platforms are 'a separate Channel row'.

**Options:**
- Add a slice to drop Channel.Provider/ExternalChannelId, and fix the comment now.
- Keep Provider as a 'first platform' marker and fix only the comment.

**Recommendation:** Add the slice after S019 and fix the comment now.

## 15. jsonb portability strategy

The docs ban jsonb behind a gate that does not exist. About 20 configs use jsonb, and ProviderCompatibilityExtensions handles SQLite.

**Options:**
- Bless the shim.
- Enforce the ban.

**Recommendation:** Bless the shim.

## 16. Legacy tables still live

The schema doc calls several tables 'replaced' but they are still mapped. Appendix items G2, G3 and C4 were never applied.

**Options:**
- Keep/fold/drop per table; retire only ChannelSubscription and Permission now.
- Fold all of them.

**Recommendation:** The first option.

## 17. Song-request target model: build or cut

The relational queue, raffle, bands, allowances, trust gate and sequencer are specced but unbuilt. Shipped: one-in-flight Add-to-Queue.

**Options:**
- Build as specced.
- Cut to shipped.
- Hybrid: bump band + PerStreamLimit.

**Recommendation:** Hybrid; the sequencer goes to the tracker as an idea.

## 18. YouTube song requests without a player

YouTubeMusicProvider accepts requests, but they never play.

**Options:**
- Refuse YouTube requests until a player exists.
- Build the IFrame player driver.

**Recommendation:** Refuse now, then build the player as its own slice.

## 19. Trust scoring configurability and PendingLimits keys

The spec has fixed weights; shipped TrustPolicy weights are editable. The subscriber keys differ between docs.

**Options:**
- Adopt shipped TrustPolicy + a single subscriber key.
- Revert to fixed weights + tiered keys.

**Recommendation:** The first option.

## 20. Music action floors

The spec floors differ from the seeds for music:config:write, library:write, token:read and control:write.

**Options:**
- Align the docs to the seeds.
- Align the seeds to the spec.

**Recommendation:** Align the docs to the seeds (per 609ae0fc2).

## 21. Platform-authored ('system') widget architecture

The plan parks a 'global system widget, config-only' initiative. What shipped is a per-channel copy + reset-to-default.

**Options:**
- Delete the initiative.
- Rewrite it as a decided slice.

**Recommendation:** Delete it.

## 22. D9 deferred items to decide or delete

Deferred framing remains in many docs, which D9 forbids:
- Plan: fire-bar parity, per-locale tone, S035 backplane, S040 cooldown write-through, S082.
- ROADMAP 'Deferred by explicit decision' items.
- commands-pipelines: Cronos, channel.views, economy tokens.
- event-store section 9.1.
- discord: tiered ping.
- federation: reserved handlers.
- gdpr-crypto items.
- scaling-qos section 8.3.
- chat-decoration: 7TV live sync.
- frontend.md: 'mobile later'.
- deployment-distribution: SaaS phases 2-3.
- frontend-ia: Templates page and 'Global search (later)' wording, if present.

**Options:**
- Decide each one as recommended.
- Turn every item into a slice.

**Recommendation:** As in the original plan's list, plus: global search becomes a tracker idea, not a spec line.

## 23. Backend libraries: logging, testing, serializer, dead references (incl. decisions-resolved Decision 7)

The stack doc binds OTel, the FluentAssertions removal, xunit.v3 and STJ-only. Code uses Serilog + file sink, FluentAssertions 8, xunit 2 and Newtonsoft. decisions-resolved Decision 7 describes logging in a self-contradicting way; the audit wanted it rewritten to 'Serilog + 30-day rolling file sink'.

**Options:**
- Bless the shipped stack, rewrite Decision 7 to match, swap only FluentAssertions → AwesomeAssertions, and delete the dead FluentValidation and Mapster references.
- Execute the stack doc fully.

**Recommendation:** The first option.

## 24. Unbuilt infrastructure decisions

HybridCache, OTel, MessagePack/backplane, KMS, Garnet, DistributedLock, Cronos, Testcontainers, OpenIddict and a DataProtection key ring are all specced but not built. StackExchange.Redis is pinned but resolves transitively.

**Options:**
- Mark them 'not built by decision'; pin Redis explicitly.
- Queue them as slices.

**Recommendation:** The first option.

## 25. Error contract: RFC 9457 vs envelope

The docs say ProblemDetails; the shipped contract is the {status, message, code, args} envelope.

**Options:**
- Keep the envelope.
- Move to RFC 9457.

**Recommendation:** Keep the envelope.

## 26. GDPR erasure of platform user ids

The docs say erasure hashes the platform id. ErasureService keeps TwitchUserId and ProviderUserId.

**Options:**
- Implement it (with the moderation carve-out).
- Amend the docs.

**Recommendation:** Implement it with the moderation carve-out.

## 27. Retention wording

The doc says 'nothing is removed on a timer'; code prunes telemetry.

**Options:**
- Reword.
- Remove the bounds.

**Recommendation:** Reword.

## 28. Channel deletion after the 30-day window

The UI promises 'unrecoverable' after 30 days, but no purge job exists.

**Options:**
- Build a purge job.
- Reword the dialog.

**Recommendation:** Reword now; decide the purge together with erasure.

## 29. Lawful basis for D4 free viewer accounts

No basis row for viewer accounts.

**Options:**
- Consent + contract via ToS.
- Legitimate interest.

**Recommendation:** Consent + contract via ToS.

## 30. Desktop token custody

The spec says OS keychain; shipped is an AES-GCM file vault with a key file beside it on desktop, and memory-only + HttpOnly cookie on web.

**Options:**
- Bless the shipped custody.
- Build OS-keychain custody as a slice.

**Recommendation:** Fix the web wording now; OS keychain as a slice.

## 31. D12 impersonation on self-host

D12 says SaaS-only, held by a 'platform owner role'. Code and memory (you reversed the mode gate 2026-08-26) say not mode-gated, gated by user:impersonate.

**Options:**
- Amend D12 to the shipped rule.
- Reinstate the self-host block.

**Recommendation:** Amend D12.

## 32. Which file is the work queue, and in what order (incl. ROADMAP header and the 2026-08-22 audit's Remediation order)

CLAUDE.md, BUILD-TODO and D8 say SHORTCOMINGS-EXECUTION-PLAN.md. ROADMAP and _GAP-AUDIT call ROADMAP 'the single live backlog'. The plan says work usability-inventory Part C. The 2026-08-22 audit and _BUILD-ORDER have older orders. Memory says stream-facing first.

**Options:**
- The plan is the single queue: inline the Part C order; ROADMAP = unsliced owner-confirm items; HISTORICAL banners on _BUILD-ORDER and the 2026-08-22 audit (delete its Remediation order section).
- Part C is the queue.

**Recommendation:** The first option. Order: open runtime/security → stream-facing → admin + authoring → the rest.

## 33. Postgres row-level security

Five docs state RLS; nothing exists.

**Options:**
- Build it plus an arch test.
- Drop it and add only an arch test that confines IgnoreQueryFilters.

**Recommendation:** Drop it plus the arch test.

## 34. Onboarding spec: ISetupService vs as-built wizard

The specs describe a state machine, guidance level and CLI; shipped is the /system/setup wizard.

**Options:**
- Rewrite to as-built.
- Build the spec.

**Recommendation:** Rewrite to as-built.

## 35. Pipeline triggers, wait_for_event, execution limits

N-trigger, MatchOn/OnTimeout, tier-scaled limits and error codes are specced; the shipped behaviour differs.

**Options:**
- Adopt the shipped semantics plus a MaxTotalActions slice.
- Build the spec.

**Recommendation:** Adopt shipped + MaxTotalActions.

## 36. Template grammar

The specs and memories use {{ }}. The resolver is single-brace, and you said so. FunCommandPresets still use {{user.name}} (checked).

**Options:**
- Single brace is final.
- Build a {{ }} resolver.

**Recommendation:** Single brace is final.

## 37. Commands-pipelines unbuilt events, CommandUsage, regex policy

8 events are unbuilt; CommandUsage is never written; the regex policy differs.

**Options:**
- Build CommandUsage + regex hardening; drop the events.
- Cut all.

**Recommendation:** The first option.

## 38. Per-platform targets for commands, timers, event responses and live-ops

D1 says per-platform targets, but no model is defined.

**Options:**
- Define it in S032.
- State 'Twitch path only' until S032.

**Recommendation:** Both: define it in S032 and label the docs until then.

## 39. Webhook security and reliability gaps

The idempotency guard, two-tier limiter, taint bag and retry cap are unbuilt.

**Options:**
- Rewrite dedupe and retry to as-built; build the taint bag + per-endpoint limits.
- Rewrite everything.

**Recommendation:** The first option.

## 40. Who may author code scripts

The spec says Broadcaster + permit; the seed says Moderator, not grantable. Jint runs in-process.

**Options:**
- Restore Broadcaster + permit.
- Keep Moderator.

**Recommendation:** Restore Broadcaster + permit.

## 41. Sandbox hardening build list

Worker process, metering, fetch allowlist, PII allowlist and Wasmtime on SaaS are all unbuilt.

**Options:**
- Queue slices in order.
- Risk acceptance.

**Recommendation:** Queue the first three now.

## 42. Script language: TypeScript or JavaScript

The spec says TS transpile; the executor is JS-only and Language defaults to 'typescript'.

**Options:**
- Add a transpile step.
- Plain JS + .d.ts.

**Recommendation:** Plain JS + .d.ts.

## 43. Moderation spec: rewrite to as-built or build the target

The target entities and services are unbuilt; the shipped split differs.

**Options:**
- Rewrite to as-built.
- Build the target.

**Recommendation:** Rewrite to as-built.

## 44. Numeric role levels in request DTOs

Several DTOs expose int levels, against the names-only rule.

**Options:**
- Replace with names.
- Accept ints.

**Recommendation:** Replace, starting with ChatFilterDto.

## 45. AppSetting (global/tenant settings table)

Three specs depend on an AppSetting table that does not exist.

**Options:**
- Drop it.
- Build it.

**Recommendation:** Drop it.

## 46. Realtime audience routing and the clock ban

Per-event Audience routing and the UtcNow ban are unbuilt.

**Options:**
- Class groups + user-lane slice; soften the clock rule.
- Enforce both as specced.

**Recommendation:** The first option.

## 47. Stream tools: presets, scheduled changes, extensions

Specced and unbuilt; the seeds are unused.

**Options:**
- One slice for all.
- Delete them.

**Recommendation:** set_stream_metadata as a slice; delete the rest.

## 48. TTS: character cap, audio store, viewer voice identity

The DTO [Range(1,500)] blocks paid-tier caps; there is no audio store; UserTtsVoice is keyed by Twitch id.

**Options:**
- Fix the cap, drop the store, re-key in Phase 5.
- Amend the spec.

**Recommendation:** The first option.

## 49. Design-system reality vs spec

Tokens, radii, palette, accent, icons, folder and catalogue check all differ from the spec. figma-design-system-rules.md is stale.

**Options:**
- Update the spec to shipped; delete the figma doc.
- Move the code back to the spec.

**Recommendation:** Update the spec to shipped.

## 50. Widgets: catalogue count, system surfaces, link preview

The seeded count, the system surfaces and link auto-embed differ from the spec.

**Options:**
- Document shipped; delete link auto-embed.
- Build the Sound surface + link auto-embed.

**Recommendation:** Document shipped.

## 51. Discord triggers with no producer

new_clip, schedule and milestone can be saved but never fire.

**Options:**
- Build the producers (S056).
- Label them in the UI now.

**Recommendation:** Label now, build in S056.

## 52. Event-store unbuilt interfaces

The replay, snapshot, shred-linker and idempotency interfaces are unbuilt; two events are never published.

**Options:**
- Delete them.
- Queue them.

**Recommendation:** Delete them.

## 53. Federation: issuer, transport and inbound endpoint

The inbound gateway has no HTTP route; the OpenIddict issuer is unbuilt.

**Options:**
- Build the inbound route.
- Declare it out of scope.

**Recommendation:** Out of scope (D8).

## 54. Live-ops mirror and pipeline actions

The mirror, events and actions are unbuilt.

**Options:**
- Cut the mirror; add 4 actions.
- Build all.

**Recommendation:** The first option.

## 55. Spam defence: pinning model, capability grid, dry run

Per-field pinning, an editable grid and the dry-run auto-flip are specced; shipped differs.

**Options:**
- Document shipped; queue replay preview.
- Build the spec.

**Recommendation:** The first option.

## 56. Stripe metered usage reporting

The report method is a no-op.

**Options:**
- Build it.
- Delete it.

**Recommendation:** Delete it for now.

## 57. Frontend architecture target (incl. the stale Koin/NavHost code comments)

Four specs describe Koin, NavHost, QueryClient, codegen and detekt; shipped is AppGraph, RouteStore, controllers and hand-written DTOs. The audit also wanted to rewrite code comments in App.kt, Destination.kt, AppGraph.kt and libs.versions.toml that still promise Koin and NavHost 'in the next slice'. These are held until this answer.

**Options:**
- Bless shipped: rewrite the specs, retire frontend-data-layer.md, and replace those four code comments with as-built descriptions.
- Queue the migration.

**Recommendation:** Bless shipped, and then fix the four comments.

## 58. EventSub SaaS webhook transport and conduit default

The webhook transport is specced but deferred; the conduit is WebSocket-based and opt-in.

**Options:**
- Delete the webhook design.
- Add a slice.

**Recommendation:** Delete it.

## 59. Who may fulfil or refund redemptions

The reward:redemption:* keys are dead; the routes use reward:manage (Broadcaster).

**Options:**
- Gate the routes with the redemption keys.
- Delete the keys.

**Recommendation:** Gate with the redemption keys.

## 60. Scaling-qos: build or cut

Only section 8 ships.

**Options:**
- Cut 2-5 and 7 to tracker ideas.
- Queue them.

**Recommendation:** Cut them.

## 61. OBS replay buffer and virtual cam floor

D6 says Broadcaster; code says obs:control (Moderator).

**Options:**
- Raise to Broadcaster.
- Change D6.

**Recommendation:** Change D6.

## 62. Community/dashboard service layer and error degrade

A failing followers call returns an empty list and a 0 count.

**Options:**
- Fix the degrade only.
- Build the service layer too.

**Recommendation:** Fix the degrade only.

## 63. Frontend IA mismatches

Event Responses placement, Settings tabs, profile menu and Admin entry differ from the spec.

**Options:**
- Update the spec; merge Alerts into Event Responses.
- Move the code to the spec.

**Recommendation:** Update the spec and merge Alerts.

## 64. Dev platform editor and phases 5-6

Monaco loads from a CDN; phases 5-6 are unbuilt.

**Options:**
- Vendor Monaco same-origin; cut phases 5-6 to ideas.
- Keep the CDN.

**Recommendation:** Vendor Monaco; cut phases 5-6.

## 65. Live-games startup widget check

A fail-closed check vs D5's art-gated games.

**Options:**
- Delete the check sentence.
- Implement it for non-art-gated games.

**Recommendation:** Delete it.

## 66. Stream Deck pairing (D7 vs shipped D9)

The code cites a D9 device-flow pairing that stream-deck.md lacks.

**Options:**
- Add D9 and supersede D7.
- Build the loopback handoff.

**Recommendation:** Add D9.

## 67. Widget SDK direction

An npm SDK is specced; shipped is window.NomNomz.

**Options:**
- Rewrite to shipped; types from EventCatalog.
- Build the npm SDK.

**Recommendation:** Rewrite to shipped.

## 68. Custom events in the event catalog, and the write floor

Per-source catalog names cannot be expressed; the floor differs.

**Options:**
- One 'custom.data' entry; Moderator floor.
- Dynamic entries; Editor floor.

**Recommendation:** The first option.

## 69. Backend taxonomy and structure linter

Contracts/<area> and Common are unnamed homes; the linter does not exist.

**Options:**
- Bless them; add a namespace = folder check.
- Relocate them and build the linter.

**Recommendation:** The first option.

## 70. Pronouns: catalog sync, cache, consent

The sync, cache and consent rules differ from the spec.

**Options:**
- Proportionate consent, cache fix, document the seeder.
- Enforce the spec.

**Recommendation:** The first option.

## 71. Media share eligibility and duration cap

The tier cap and eligibility are unbuilt.

**Options:**
- Expose subOnly + tier cap.
- Shrink D3/D4.

**Recommendation:** Expose subOnly + the tier cap.

## 72. Sound clip storage and upload limits

Disk only, fixed limits, no upload rate limit.

**Options:**
- Document shipped + write-expensive rate limit.
- Build the object store.

**Recommendation:** The first option.

## 73. Engagement: column naming and state while off

The ViewerTwitchUserId names are legacy; no state is recorded while triggers are off.

**Options:**
- Rename the columns and always upsert state.
- Keep and document.

**Recommendation:** The first option.

## 74. Canonical deploy path and colour overlap

Three blue/green implementations drift apart.

**Options:**
- CI calls switchover.ps1; ship.ps1 = sync + verify.
- ship.ps1 canonical.

**Recommendation:** The first option.

## 75. Live overlay token rotation

e2e-authed-app-access.md holds a plaintext overlay token and real ids. This plan removes them from memory. Rotating Channel.OverlayToken on the live box is a production change: every OBS browser source that uses the old URL stops until it is updated.

**Options:**
- Rotate it now and update your OBS sources.
- Do not rotate (memory is local-only); just remove the plaintext.

**Recommendation:** Rotate it at a quiet moment, since it was stored in plaintext. You choose the timing.

## 76. wolfwave song-request code

The grant is a chat screenshot; wolfwave's licence is unrecorded.

**Options:**
- Get it in writing + check licence compatibility.
- Ideas only.

**Recommendation:** Get it in writing first.

## 77. Drop game mechanic

Pure RNG; a redesign is requested.

**Options:**
- Skill-based timing.
- Keep RNG, improve the visuals.

**Recommendation:** Skill-based timing.

## 78. Historical design docs

2026-06-18-implementation-workflow.md and 2026-06-17-saas-architecture-flow.md no longer describe how work or the runtime happens. The audit also wanted to delete 2026-06-16-frontend.md and 2026-06-16-frontend-structure.md, but both carry explicit 'kept for history / research record' SUPERSEDED headers.

**Options:**
- Delete the workflow doc (move its DoD into dispatch-a-builder); label saas flow sections 1-2 TARGET; keep the two frontend history docs as they are.
- Keep everything with HISTORICAL banners.

**Recommendation:** The first option.

