<!-- Audit deltas for 2026-06-16-database-schema.md. Apply when locking the final schema. Source: subagent audit 2026-06-16. -->

# Schema — Audit Deltas to Apply

Findings from the review of [2026-06-16-database-schema.md](2026-06-16-database-schema.md). Apply these before the schema is locked.

## Coverage gaps — add tables

- **EarningRules** (tenant): per-source (watch-time / chat / follow / sub / bits / raid / redemption) rate + cap. `CurrencyConfig` only holds name/start-balance — earning is currently undefined data.
- **StreamPreset** + **ScheduledStreamChange** (tenant): per-game title/game/tag presets; scheduled title/game/tag changes. `Channels` holds only *current* values.
- **EventSubConduit** (global): SaaS conduit id + shard count + shard assignments, app-global, survives restart. `EventSubSubscriptions` only stores a per-sub `ConduitId` string — can't represent the shared conduit.
- **UserPreferences** (per-user): holds `GuidanceLevel` — onboarding doc says per-user and adjustable anytime, but it currently lives only on the global `DeploymentProfile`.
- **ChannelBuiltinCommand** (tenant): enable/disable state for seeded/built-in commands (e.g. `!followage`). `Commands` is for *authored* commands. This is the CLAUDE.md "commands show 0 / seeding skipped" known issue — still unmodeled.
- **NetworkNukeBatch** (+ link to `ModerationActions`): the set of channels one nuke hit, so "un-nuke" reverses as one unit. A per-channel action row can't enumerate the fan-out.
- Confirm **UsageRecord** carries sandbox exec-ms (`TierLimit.sandbox_exec_ms` quota) — else add exec-ms metering.

## Contradictions — fix

- `RewardRedemptions.EventId` & `CurrencyLedgerEntries.EventId` "correlate to `EventJournal.EventId`" but are bare `guid`, not FKs (EventJournal PK is `bigint Id`; `EventId` is unique guid). Declare FK→`EventJournal.EventId` (valid, it's unique) or mark explicitly unenforced.
- FKs hidden in JSON blobs → make join tables (a blob can't FK / index / join / cascade-on-erasure):
  - `SharedBanSettings.TrustedChannelsJson` (`List<guid>`→Channels)
  - `ViewerReports.EvidenceMessageIds` (`List<long>`→ChatMessages)
- `EventJournal (BroadcasterId, StreamPosition)`: make **UNIQUE**, not Index — idempotent replay requires it.
- `Users.Pronoun`: keep the existing **Pronoun lookup table** (grammar attrs: subject/object/…), do NOT collapse to an enum (TTS/pronunciation needs the attributes). Still Art. 9 special-category.

## Portability traps — fix (model must run on SQLite)

- **Live `jsonb` in code:** `ChannelConfiguration.cs` uses `.HasColumnType("jsonb")...::jsonb`. The spec's claim that `Channel.Tags` is "already portable" is **false**. Every `[VC:JSON]` is net-new converter work; no SQLite provider is wired (`UseNpgsql` only). Wire portable converters + the SQLite provider.
- **Per-tenant monotonic `bigint`** (`EventJournal.StreamPosition`, `CurrencyLedgerEntries.Id`): PG/SQLite auto-increment is GLOBAL, not per-tenant; SQLite has no sequences. App-assign under a per-tenant lock — define how (races otherwise).
- **Indexed decimal sort** (`UserTrustScores.TrustScore/HeatScore`, `SongRequestQueues.MinTrustScore`): SQLite decimal = TEXT/REAL can mis-sort range/ORDER BY. Use scaled int (basis points) or accept REAL affinity.
- **Case-insensitive uniqueness:** add `*Normalized` lowercase column + index for `Users.Username`, `Channels.Name`, `Commands.Name`, `CatalogItems.Name`. Spec promised this but defined none → otherwise `citext`/`LOWER()` = Postgres-only.
- **`TtsCacheEntry.AudioData` blob in-row:** add a `StorageRef` (disk/object store) — SQLite has no out-of-line storage; avoids page bloat + a later migration.

## Owner decisions (load-bearing)

1. **Surrogate `guid` PKs everywhere; Twitch ids → indexed attributes; `ITenantScoped.BroadcasterId` widened `string`→`Guid`.** **DECIDED — adopt.** Keys are **UUIDv7** via native `Guid.CreateVersion7()` (.NET 9+): time-ordered / index-friendly like a ULID, zero 3rd-party lib. Twitch user/channel ids stay first-class indexed columns (fully usable consumer-side and for all Helix calls); the guid is internal FK + GDPR-shred only. One-time clean-slate rebuild; enables O(1) cascade-safe erasure.
2. **PII erasure** = crypto-shred DEK (O(1)) for `[PII-shred]`. BUT `[PII-scrub]` plaintext snapshots (`*DisplayNameSnapshot`/`*UsernameSnapshot`) across ~20 append-only tables need a `(BroadcasterId, SubjectUserId)` index each, or "O(1) erasure" is false for scrub. → add those indexes.
3. **EventJournal one `SubjectKeyId` per row can't shred multi-subject events** (gift sub: gifter+recipient; raid: raider+raided). → add `EventSubjectKeys` link table OR enforce one-subject-per-event. Real GDPR hole.
4. **Cross-tenant isolation** (SavingsJars, federation, shared bans) via membership/trust predicates — described in prose only. → write + test the concrete EF filter / RLS predicate (leak risk if wrong).
5. **Tenant isolation (EF global query filter + RLS) NOT yet wired** — `OnModelCreating` applies none today. → wire it + prove with a tenant-A-can't-read-tenant-B test before claiming isolation.
6. **App config JSON blobs** (`PipelineSteps.ConfigJson`, `Commands.TemplateResponses`, `Timers.Messages`, `EventResponses.MetadataJson`, `AppSetting.Value`) have no schema version. → add `ConfigSchemaVersion int` (event side has `EventVersion`; config side has nothing).
