<!-- Source: multi-agent workflow wf812c7r8 (db-schema-full-spec), 2026-06-16. Authoritative DB schema. -->

**STATUS: LOCKED 2026-06-16**

## Changelog — audit deltas applied

Every item from `2026-06-16-database-schema-AUDIT.md` applied as targeted edits at lock. Locked key decision: **all surrogate PKs are `guid` via UUIDv7 (`Guid.CreateVersion7()`), Twitch ids are first-class indexed attribute columns, tenant key `BroadcasterId` is `guid`** (§1.1).

**Scaling/QoS additions (`spec/scaling-qos.md`):**
- `CommandLogEntry` (O.11) — durable log-first intake/work queue (append→claim→process); sibling to `EventJournal` (the outcome/fact log).
- `TierLimit` keys += `worker_concurrency`, `rate_api_per_min`, `rate_command_per_min`, `rate_webhook_in_per_min`, `rate_song_request_per_min` (per-tenant fair-scheduling concurrency cap + inbound rate caps).

**Coverage — 7 tables added:**
- `EarningRules` (K.1a) — per-source earn rate + caps (was undefined data; `CurrencyConfig` only held name/start-balance).
- `StreamPresets` (F.10) + `ScheduledStreamChanges` (F.11) — per-game title/game/tag presets + scheduled changes (`Channels` holds only current values).
- `EventSubConduits` (F.8) + `EventSubConduitShards` (F.9, GLOBAL) — app-global SaaS conduit id/shard-count/shard-assignments that survive restart.
- `UserPreferences` (R.2, per-user) — holds per-user, adjustable-anytime `GuidanceLevel` (was only on global `DeploymentProfile`).
- `ChannelBuiltinCommands` (G.2a) — enable/disable + override state for seeded/built-in commands; closes the CLAUDE.md "commands show 0 / seeding skipped" known issue.
- `NetworkNukeBatches` (J.2a) + `ModerationActions.NetworkNukeBatchId` link — groups one nuke's fan-out so un-nuke reverses as one unit.
- Confirmed `UsageRecord.MetricKey` meters `sandbox_exec_ms` (matches `TierLimit` quota).

**FK-in-JSON-blob → join tables:**
- `SharedBanSettings.TrustedChannelsJson` → `SharedBanTrustedChannels` (J.9a).
- `ViewerReports.EvidenceMessageIds` → `ViewerReportEvidence` (J.8a).

**Contradictions fixed:**
- `EventJournal (BroadcasterId, StreamPosition)` is now **UNIQUE** (was Index) for idempotent replay.
- `RewardRedemptions.EventId` / `CurrencyLedgerEntries.EventId` are now enforced FKs → `EventJournal.EventId` (valid; it's Unique).
- `Pronouns` kept as a **lookup table** (R.1, grammar attrs for TTS); `Users.PronounId` FKs it — not collapsed to an enum. Still Art. 9 special-category.

**Portability traps fixed (model runs on SQLite):**
- `Microsoft.EntityFrameworkCore.Sqlite` provider wired by DI adapter; native `jsonb`/`HasDefaultValueSql` banned; every `[VC:JSON]` is a real converter (§1.4). Corrected the false "`Channel.Tags` already portable" claim.
- Per-tenant monotonic sequence defined: app-assigned under a per-tenant lock via `TenantSequences` (Q.3).
- `*Normalized` lowercase column + unique index on `Users.Username`, `Channels.Name`, `Commands.Name`, `CatalogItems.Name`.
- `TtsCacheEntry.StorageRef` added for out-of-row audio.
- `ConfigSchemaVersion int` added to every app-interpreted JSON-config table (`PipelineSteps`, `Commands`, `Timers`, `EventResponses`, `AppSetting`, `EarningRules`, `ChannelBuiltinCommands`, `UserPreferences`).
- `(BroadcasterId, SubjectUserId)`-style indexes added on snapshot/append-only tables (`RewardRedemptions`, `ChatMessages`, `ModerationActions`, `GamePlays`, `CatalogPurchases`, `WatchSessions`, `CommandUsage`, `LeaderboardSnapshots`, `CurrencyLedgerEntries`).
- `EventSubjectKeys` (O.1a) link table added for multi-subject (gift-sub / raid) event shred.
- `decimal` trust/heat scores documented as SQLite REAL-affinity.

**Additional load-bearing column adds:** `Channels.Status`/`SuspendedAt`/`SuspendedReason`; `Subscriptions.TrialEndsAt`/`GracePeriodEndsAt`; `Users.IsBot`/`LastSeenAt`; `IntegrationConnections` refresh-health columns; `AppSetting.ValueType`; `FeatureFlag.MinTierId` FK; `ConsentRecords` unique `(BroadcasterId, SubjectUserId, ConsentType)`; `RefreshTokens (UserId, RevokedAt)` index.

**Cross-spec seam resolutions (interface-spec consistency pass):**
- `TtsApprovalQueueEntry` (P.1a) added — tenant-scoped, soft-delete, `pending`\|`approved`\|`rejected`\|`expired` status; backs the TTS mod-approval queue (resolves finding B4; owner `tts.md`).
- `Channels.SongRequestPageToken string(64) Null Unique` added — rotatable public song-request page token, mirrors `OverlayToken` (resolves finding B5; owner `music-sr.md`).
- `SongRequestQueues.MinTrustScore` (L.4) renamed → `MinYouTubeTrustScore decimal(8,4) Null` — broadcaster-configurable minimum trust for YouTube request auto-approval (Spotify is not trust-gated); `Vip`/`Moderator`-and-above community standing bypasses the gate via role (owner `music-sr.md`).
- `SongRequestQueues` (L.4) reworked for interleaved dual-provider playback + tiered allowances (owner `music-sr.md`): **removed** flat `MaxPerUser`; **added** `EnabledProviders text [VC:JSON] List<string>` (which of `spotify`/`youtube` accept requests; `ProviderPriority` redefined as preferred-provider-for-ambiguous-requests + cross-resolve target), `CrossResolveForeignLinks bool` (default true — resolve a foreign-provider link by metadata and re-search on the target), `PendingLimits text [VC:JSON] Dictionary<string,int?>` (per-standing concurrent-pending cap, keys `everyone`/`subscriber_t1`/`subscriber_t2`/`subscriber_t3`/`vip`/`moderator`/`broadcaster`, `null`=unlimited; defaults 2/4/4/4/10/unlimited/unlimited), `PaidPendingLimit int Null` (separate cap for channel-point requests, null=off), `PaidExtraSlotEnabled bool` (default false — `extra-slot` redeem adds a fair-position request bypassing the free cap), `QueueJumpEnabled bool` (default false — priority-placement redeem), `PerStreamLimit int Null` (lifetime-in-stream per-user cap, null=off), `MaxDurationFreeSeconds int` (default 360), `MaxDurationPaidSeconds int` (default 600), `MinStandingToRequest string(20) [VC:enum]` (community-standing floor to submit; default `everyone`), `StripYouTubeAds bool` (default true — overlay player blocks YouTube ad requests), `SpotifyLockedDeviceId string(255) Null` + `SpotifyLockedDeviceName string(255) Null` (remembered preferred Spotify device for drip-feed playback + the connection nudge). Kept `MaxQueueLength`/`SubscriberOnly`/`AllowExplicit`/`ProviderPriority`.
- `SongRequestItems.Status` (L.5) `[VC:enum]` extended with `waiting` (provider/environment unavailable — indefinite, skipped by the playable-head rule, never auto-removed) + `retrying` (per-item transient error on a healthy provider — bounded backoff); **added** `RetryCount int` (default 0), `FailureReason string(100) Null`, `NextRetryAt timestamp Null` (owner `music-sr.md`).
- `SongRequestQueues.TrustScoringConfig text Null [VC:JSON]` (L.4) **added** — advanced per-channel buff/debuff tuning for the §3.9 Bamo trust algorithm (per-modifier toggle + magnitude: reputation boost, follow penalty, YouTube channel-quality penalty group, skip/timeout/ban penalties); defaults reproduce Bamo's current constants exactly so default behavior is unchanged, the metric base (weights/decays) stays FIXED and unstored, `MinYouTubeTrustScore` kept; mirrors the `PendingLimits` JSON precedent (owner `music-sr.md`).
- Bump tier + song-bump raffle (owner `music-sr.md`): `SongRequestQueues` (L.4) **added** `AutoBumpFirstSong bool` (default false), `RaffleEnabled bool` (default false), `RaffleEntryCost int` (default 0), `RaffleTicketsPerUser int` (default 1), `RaffleWinnerCount int` (default 1), `RaffleIntervalMinutes int Null` (null = manual-only). `SongRequestItems` (L.5) **added** `PriorityBand string(20) Index [VC:enum]` (`bump`\|`auto_bump`\|`normal`, default `normal` — three-band ordering, bands stack bump→auto_bump→normal, `Position` orders within a band) + `BumpSource string(20) Null [VC:enum]` (`raffle`\|`command`\|`redeem`). **3 tables added** beside the SR siblings: `SongRequestRaffles` (L.7, soft-delete — one open raffle per channel, config snapshotted at open), `SongRequestRaffleEntries` (L.8, append-only — one entry per (raffle, user) + channel-point `CatalogPurchaseId` link mirroring L.5, winners flagged at draw), `SongRequestBumpTokens` (L.9 — per-channel per-user bump-token balance granted when a winner has no queued song, consumed on next request). Raffle entry reuses the economy `CatalogPurchases` debit pattern (no new economy primitive — none existed; see `music-sr.md` §3.11).- `SpotifyIntegrationConfig` (E.5) + `YouTubeIntegrationConfig` (E.6) collapsed into one generic `MusicProviderConfig` (E.5, soft-delete) — `Provider string(30)` registry key + shared columns (`AllowSongRequests`/`MaxQueueLength`/`BlockExplicit`) + `ProviderSettings text [VC:JSON]` for provider-specific knobs, **Unique** `(BroadcasterId, Provider)`. `IntegrationConnections.Provider` clarified as an OPEN registry key (music providers self-register capabilities + settings schema via `IMusicProviderRegistry`), not a closed `spotify|youtube` enum. New providers slot in with zero new tables/migrations (owner `music-sr.md`).
- `EventJournal.Source` (O.1) enum extended with `federation` — events ingested across federated/relayed deployments are first-class, distinct from `eventsub`\|`domain`\|`irc`\|`import`.
- `TtsConfig` (P.1) BYOK keys migrated to the `IntegrationTokens` (E.2) AEAD envelope: `AzureApiKeyCipher`/`Nonce`/`KeyVersion` and `ElevenLabsApiKeyCipher`/`Nonce`/`KeyVersion` replace the single-text key columns; AAD = `tenantId\|provider\|tokenType\|keyVersion` binds each ciphertext to its tenant/provider/version so a stale key can't be replayed.

**Commands/counters/prefix additions (owner `commands-pipelines.md`):**
- `NamedCounters` (G.4) added — tenant-scoped, soft-delete persistent cross-command counter store (`Key string(50)`, `Value bigint`, **Unique** `(BroadcasterId, Key)`); backs `{{count.<name>}}` + the `set_counter`/`adjust_counter` pipeline actions. Closes the catalog's "named counters have no data source" open question.
- `Commands` (G.2) per-command trigger model added — `PrefixMode string [VC:enum]` (`Default`\|`Custom`\|`None`), `CustomPrefix string(8) Null` (used when `Custom`), `MatchMode string [VC:enum]` (`StartsWith`\|`Exact`\|`Contains`\|`Regex`, default `StartsWith`), `MatchPattern string(200) Null` (author regex; required only when `MatchMode=Regex`). Built-ins default `PrefixMode=Default`. `Regex` is a first-class match mode made ReDoS-safe by .NET's own `RegexOptions.NonBacktracking` engine (linear-time, no Wasmtime/Jint sandbox) — validated + compiled at save via `IRegexMatcher` (`commands-pipelines.md` §6.4).
- `Channels` (A.2) `DefaultCommandPrefix string(8)` added (default `!`) — the channel-level default command prefix; effective prefix for any `Commands.PrefixMode=Default`. Home = `Channels` (read on every dispatch hot path; avoids an `AppSetting` lookup per message). Surfaced as `{{bot.prefix}}`.

**Monetization authoring-count quota additions (owner `monetization-billing.md` §8):**
- `TierLimit.LimitKey` (N.2) `[VC:enum]` set extended with four authoring-count keys — `response_variations_per_trigger` (per-trigger cap on response-variation count: `Command.TemplateResponses` / `random_response.Messages` on event-responses + reward-redemption responses), `custom_commands`, `timers`, `event_responses` (per-tenant trigger-count caps). Same N.2 mechanism, `LimitValue bigint` (`-1`=unlimited), `IBillingTierService.GetEntitlementAsync` map; **no new table/column**. Seeded by `DataSeeder` (indicative `response_variations_per_trigger` 5/15/40/100 across free/base/pro/premium; self-host resolves all to `-1`). Meters quantity only — template expressiveness is untiered. Add-time enforcement in `ICommandService`/`IEventResponseService`/`ITimerService` (`Result.Failure("tier_limit_reached", …)`), grandfathered on downgrade.
- `TierLimit.LimitKey` (N.2) `[VC:enum]` set extended with `tts_max_characters` — the per-utterance TTS character cap (safety baseline + tier headroom; absolute ceiling 8000), read by `tts.md` via `IBillingTierService.GetLimitAsync`.

**No-free-hosted-tier + tier-scaled limits (owner `monetization-billing.md` / `scaling-qos.md`):**
- `BillingTier` (N.1) — hosted/SaaS is **paid-only, no free hosted tier**. Public hosted plans seeded `base`/`pro`/`premium` at `PriceCents` `399`/`799`/`1499` (`IsPublic=true`). The `free` row is retained as a **non-public** (`IsPublic=false`, `PriceCents=0`) internal marker for self-host / unbilled installs only — never a cloud plan. `AllowsCustomBotName` seeded **true for `pro`+ only** (`base` shared platform bot; self-host always custom). No column change — seed-policy + Purpose note.
- `TierLimit` (N.2) — per-tier seed rows now cover **`base`/`pro`/`premium` only** (no hosted `free` rows; self-host gets none and resolves all to `-1`). Every limit is a safety baseline + tier-scaled headroom; `sandbox_exec_ms`, `worker_concurrency`, and the `rate_*` keys are tier-scaled (`base`<`pro`<`premium`). Supersedes the earlier "5/15/40/100 across free/base/pro/premium" seed note (the `free` hosted column is dropped: `base`/`pro`/`premium` = 15/40/100 etc.). No column change — seed-policy + Purpose note.

**Sandbox egress request-cap additions (owner `custom-code.md` / `code-execution-sandbox.md`):**
- `HttpEgressAllowlist` (H.7) outbound-request controls added — `MaxRequestBytes int` (default 8192, **reject** when exceeded, not truncate), `AllowRequestBody bool` (default false), `AllowedMethods string(100)` (CSV of permitted HTTP methods, default `GET`), `PathPrefix string(255) Null` (optional path-prefix restriction, null = any path). The prior schema capped only the response (`MaxResponseBytes`); these clamp outbound exfil and scope second-order/confused-deputy SSRF per-row (`code-execution-sandbox.md` §7.1 step 6b/step 9, §7.4).
- `HttpEgressAllowlist` (H.7) `AllowQuery bool` (default false) added — a guest may attach an arbitrary query string to an egress request only when opted in (second-order SSRF reduction; `custom-code.md` §1 / §7.1).

**Webhooks sub-domain added (owner `webhooks.md`):**
- `OutboundWebhookEndpoints` (H.8, soft-delete) + `OutboundWebhookDeliveries` (H.9, APPEND-ONLY delivery/retry/dead-letter log) + `InboundWebhookEndpoints` (H.10, soft-delete) added under Domain H beside `HttpEgressAllowlist` (H.7). User/third-party webhooks, distinct from Twitch EventSub. Signing/verification secrets follow the AEAD envelope pattern (`*Cipher`/`*Nonce`/`EncryptionKeyId`, AAD binds tenant+endpoint) like `IntegrationTokens` (E.2). Inbound token mirrors the `Channels.OverlayToken` opaque-token model.
- `EventJournal.Source` (O.1) enum extended with `webhook` — a verified third-party inbound webhook becomes a first-class journal event (`Source=webhook`, `EventType="webhook.<provider>.<kind>"`, deterministic `EventId` from the provider event id), distinct from `eventsub`\|`domain`\|`irc`\|`import`\|`federation`.
- **Reuse, not duplication:** outbound SSRF reuses `HttpEgressAllowlist` (H.7, extended only by the already-present request-cap columns — no webhook-specific column); inbound dedup + outbound idempotency reuse `IdempotencyKey` (O.4, `Scope="webhook:in:{endpointId}"` / `"webhook:out"`) — no new dedupe table; inbound journal ordering reuses `TenantSequences` (Q.3).

**Import/Export & Marketplace domain added (owner `marketplace.md`):**
- `InstalledBundle` (H.11, soft-delete) added under Domain H beside `InboundWebhookEndpoints` (H.10) — tracks an installed import/marketplace bundle for update/uninstall (`Name string(150)`; `Source string(20)` [VC:enum] `local`\|`marketplace`; `MarketplaceItemId string(64) Null` null for local ZIPs; `Version string(40)`; `Author string(100) Null`; `License string(40) Null`; `ManifestJson text` [VC:JSON] the `BundleManifest`; `InstalledEntityIdsJson text` [VC:JSON] `{ type → Guid[] }` for update/uninstall; `InstalledByUserId guid` FK→`Users`; **Unique** `(BroadcasterId, Source, MarketplaceItemId)`, **Index** `BroadcasterId`). The marketplace catalog itself lives in the separate NoMercy-hosted marketplace service.

**18+ gambling-gate provable-adult inference (owner `economy.md`):**
- `GameConfigs.Requires18Plus` (K.7) is **default `false`**, and the age gate is reframed as an **optional, off-by-default streamer toggle over fun-money** (non-purchasable, non-cashable currency → not regulated gambling, no mandatory 18+), **not** a compliance/KYC requirement. Age/18+ status is treated as **regular personal data, not Art. 9 special-category** (special-category remains pronoun-only); the K.8 cache and `gdpr-crypto.md` §3.6 carry no extra-care/special-category framing for the age gate. When `Requires18Plus=false` (default), plays run with no age check; the inference+self-confirm path engages only when a streamer opts in.
- `ViewerAgeConsents` (K.8) extended so adults aren't always forced through an explicit consent prompt. `ConfirmationMethod` enum gains `inferred_account_age` (PRIMARY) + `inferred_twitch_personnel` (secondary), alongside the existing `chat_command`\|`dashboard`\|`overlay`. New columns: `LawfulBasis string(30)` (`legitimate_interest` for inferences, `consent` otherwise), `InferredAccountCreatedAt timestamp Null` (immutable `Users.CreatedAt` snapshot for the account-age method), `InferredFromStatus string(20) Null` (snapshotted Twitch `type` for the personnel method), `StatusVerifiedAt timestamp Null` (re-check stamp for the revocable personnel status; unused by the monotonic account-age method); `ConsentRecordId` relaxed to `Null` (inferences carry no consent-ledger row). Account-age threshold = configurable `Age18AccountYears` (code default `7`y; `≥5`y is the proven floor since Twitch min signup age is 13; overridable via `AppSetting` P.11 `economy/age18_account_years`). An inference is **never** written to `ConsentRecords` (O.5) — the consent ledger keeps meaning "the human affirmatively consented"; the inference lives in the K.8 cache only, kept auditable + visibly distinct via the snapshot columns. Affiliate/Partner/broadcaster are excluded as adulthood signals (Twitch permits 13–17 minors to hold them). Gate fails closed on unknown `created_at`/`type`.

**Permanent storage — auto-purge/retention layer removed (owner `gdpr-crypto.md`):**
- Data is stored **permanently for everyone**; PII is removed **only** by manual crypto-shred erasure-on-request — retention is never tiered, never auto-purged.
- Removed `Channels.RetentionDays` (A.2) — no per-channel auto-purge override.
- Removed `event_retention_days` from the `TierLimit.LimitKey` (N.2) enum — retention is not a usage limit; all other usage keys unchanged.
- Removed the `RetentionPolicy` table (was O.7) entirely, and its `RetentionPolicy(when non-null)` entry from the §4 tenant-scoped list.
- Removed `retention_purge` from the `ComplianceAuditLog.RequestType` (O.10) enum and "retention" from its Purpose (no purge events to audit).
- `ChatMessages` (G.1) `CreatedAt` note + Purpose retag from retention-purge to erasure-scrub; §5 D2 index note + §"Retention/minimization" paragraph rewritten to permanent-storage + crypto-shred-only.

**CryptoKey versioning (owner `gdpr-crypto.md`):**
- `CryptoKey` (Q.1) `KeyVersion int` added (default `1`, incremented on rotation) — the version field `CipherAad.KeyVersion` (`gdpr-crypto.md` §4.1) is sourced from; previously `CipherAad` referenced a non-existent column. Bound into the AEAD AAD so ciphertext can't be replayed under a rotated key.

**First-party widget catalogue provenance (owner `widgets-overlays.md` §1.1):**
- `WidgetGalleryItem` (P.8) `SourceKind string(20) [VC:enum]` (`in_repo`\|`github`) added — discriminates the seeded first-party catalogue (source shipped in-repo under `web/widgets/{key}/`) from GitHub-pinned community submissions. `GitHubRepoUrl`/`PinnedCommitSha` relaxed to `Null` (NULL for `in_repo` rows); the `(GitHubRepoUrl, PinnedCommitSha)` Unique now applies to `github` rows only (partial/filtered). `SubmitterUserId`/`SubmitterTwitchUserId` relaxed to `Null` — the twelve first-party rows are platform-owned (`SubmitterUserId=null`). Seeded idempotently by `FirstPartyWidgetCatalogueSeeder` (`TrustTier=first_party`, `ReviewStatus=verified`, `AvailableInSaaS=true`).

**Live-games session domain added (owner `live-games.md`):**
- `GameSessions` (K.9a, soft-delete) added beside `GamePlays` (K.9) — a stateful interactive overlay-game round (`Status` `lobby`\|`running`\|`resolving`\|`settled`\|`cancelled` [VC:enum]; `StateJson`/`OutcomeJson` [VC:JSON] for the overlay frame + crash recovery; **Index** `(BroadcasterId, Status, CreatedAt)`). At most one non-terminal row per `BroadcasterId` is **service-enforced, NOT a DB unique constraint** (terminal rows accumulate). Instant economy games (`GamePlay` via `IGameService.PlayAsync`) create no session.
- `GamePlays` (K.9) `GameSessionId guid FK→GameSessions Null Index` added — null for instant `PlayAsync` games, set for every live-game award row; `(GameSessionId)` Index added for session-history reads.

**Quotes + OBS control domains added:**
- `Quotes` (G.5, soft-delete) added beside `NamedCounters` (G.4) — numbered, searchable channel quotes (`Number int` allocated via `TenantSequences` Q.3, never reused; **Unique** + **Index** `(BroadcasterId, Number)`); backs the `!quote` built-in and the `post_quote` pipeline action (owner `quotes.md`).
- `ObsConnections` (P.14, soft-delete) added beside `FeatureFlag` (P.13) — per-channel OBS WebSocket v5 connection config (**Unique** `BroadcasterId`, one per channel; `Mode` `direct`\|`bridge`; `PasswordCipher` AEAD via `IFieldCipher`; `BridgeToken` for the browser-source control bridge; `EventSubscriptionsMask` for OBS-event triggers). Direct on self-host; browser-source bridge with single-executor election on SaaS (owner `obs-control.md`).
- `VtsConnection` (P.19, soft-delete) added beside `SoundClip` (P.18) — per-channel VTube Studio connection config (**Unique** `BroadcasterId`, one per channel; `Mode` `direct`\|`bridge`; `Endpoint` default `ws://localhost:8001`; `PluginTokenCipher` AEAD via `IFieldCipher`; `BridgeToken` for the SaaS relay; `EventSubscriptionsMask` for `vts_event` triggers). Direct on self-host; OBS-relay browser-source bridge on SaaS (owner `vtube-studio.md`).

**Giveaways domain added (owner `giveaways.md`):**
- `Giveaways` (G.6, soft-delete) added beside `Quotes` (G.5) — a giveaway campaign (`EntryMode` `keyword`\|`active_viewers`, `PrizeMode` `announce`\|`currency`\|`pipeline`\|`code_pool`, `Status` `draft`\|`open`\|`closed`\|`drawn`\|`archived` [VC:enum]; `EligibilityJson`/`WeightingJson` [VC:JSON]; FKs `PrizePipelineId`→`Pipelines`, `PrizeCodePoolId`→`GiveawayCodePools`; **Index** `(BroadcasterId, Status)`).
- `GiveawayEntries` (G.7, soft-delete) added — one entry per viewer per giveaway (`ViewerUserId` FK→`Users`, `ViewerTwitchUserId` [PII-hash], weighted `TicketCount`; **Unique** `(GiveawayId, ViewerUserId)`).
- `GiveawayWinners` (G.8, APPEND-ONLY) added — winner history per draw (`Status` `drawn`\|`claimed`\|`forfeited`\|`redrawn` [VC:enum], `IsRedraw`, `AssignedCodeId` FK→`GiveawayCodes`; **Index** `(GiveawayId)`, `(BroadcasterId, DrawnAt)`).
- `GiveawayCodePools` (G.9, soft-delete) added — a named pool of single-use prize codes.
- `GiveawayCodes` (G.10, soft-delete) added — a single-use secret prize code, `CodeCipher` AEAD-encrypted via `IFieldCipher` ([PII-shred], never plaintext at rest or in reads); `Status` `available`\|`assigned`\|`delivered`\|`revoked` [VC:enum]; **Index** `(CodePoolId, Status)`. Economy delta: `CurrencyLedgerEntry.EntryType` gains `spend_giveaway`/`earn_giveaway` (owner `economy.md`).

**Engagement-triggers domain added (owner `engagement.md`):**
- `EngagementConfigs` (G.11, soft-delete) added beside `GiveawayCodes` (G.10) — per-channel engagement-trigger config (`FirstTimeChatterEnabled`/`ReturningChatterEnabled`/`WatchStreakEnabled` all default false, opt-in; `StreakMilestonesJson` [VC:JSON] `int[]`; `GreetCooldownSeconds` default 5; **Unique** `BroadcasterId`, one per channel).
- `ViewerEngagementStates` (G.12, soft-delete) added — per-viewer engagement detection state for first-chat/streak (`ViewerUserId` FK→`Users`, `ViewerTwitchUserId` [PII-hash]; `FirstChatAt`/`LastChatAt`; `LastSeenStreamSessionId`/`LastGreetedStreamSessionId` greet-dedup; self-owned `ConsecutiveStreams` streak counter; **Unique** `(BroadcasterId, ViewerUserId)`).

**Custom-events domain added (owner `custom-events.md`):**
- `CustomDataSources` (G.13, soft-delete) added beside the engagement siblings (G.11/G.12) — per-channel custom data source producing normalized `custom.<name>` events (`Name string(50)` slug; `SourceKind` `push`\|`poll`\|`socket` [VC:enum]; `PresetKey` null = hand-rolled; `EndpointUrl` poll/socket URL; `AuthSecretCipher` [PII] AEAD via `IFieldCipher`; `FieldMapJson` [VC:JSON] `{ "<field>": "<jsonpath>" }`; `PollIntervalSeconds` poll-only tier-floor; `InboundWebhookEndpointId` FK→`InboundWebhookEndpoint` push-only; `IsEnabled` default false opt-in; `LastReceivedAt`; **Unique** `(BroadcasterId, Name)`, **Index** `BroadcasterId`). Latest value cached (not stored), history in the event journal. Webhook delta: `InboundWebhookEndpoint.AdapterKind` (H.10) gains `customdata`.

**Per-viewer data store added (owner `per-viewer-data.md`):**
- `ViewerData` (G.14, soft-delete) added beside `CustomDataSources` (G.13) — writable per-viewer key/value store, the per-viewer analog of `NamedCounter` (G.4) (`ViewerUserId` FK→`Users`; `Key string(50)` slug; `Value text` string, numeric ops parse/format as `long`; **Unique** `(BroadcasterId, ViewerUserId, Key)`, **Index** `BroadcasterId`, `ViewerUserId`). Backs the `set_viewer_data`/`adjust_viewer_data` pipeline actions + `{{viewer.data.<key>}}`/`{{target.data.<key>}}` helpers; erasure deletes a subject's rows by `ViewerUserId`.

**Media-share domain added (owner `media-share.md`):**
- `MediaShareConfigs` (L.10, soft-delete) added beside the song-request siblings (L.4–L.9) — per-channel viewer clip/video queue config (`RequireApproval` default true, `AllowTwitchClips`/`AllowYouTube` default true, `MaxDurationSeconds` default 180 hard cap, `EntryCost bigint Null` + `EligibilityJson` [VC:JSON] optional cost/eligibility, `MaxQueueLength` default 20, `PerUserCooldownSeconds` default 60, `ConfigSchemaVersion`; **Unique** `BroadcasterId`, one per channel). Distinct from music song-requests (audio) — this queues short embeddable **video** clips played on an overlay.
- `MediaShareRequests` (L.11, soft-delete) added — one submitted clip/video (`RequesterUserId` FK→`Users`, `RequesterTwitchUserId` [PII-hash]; `SourceType` `twitch_clip`\|`youtube` [VC:enum]; `SourceUrl`/`MediaRef`/`Title`/`DurationSeconds`/`ThumbnailUrl` server-fetched metadata; `Status` `pending`\|`approved`\|`rejected`\|`playing`\|`played`\|`skipped` [VC:enum]; `QueuePosition int Null`; `EntryCostLedgerEntryId bigint Null`; `RequestedAt`/`DecidedAt`/`DecidedByUserId`; **Index** `(BroadcasterId, Status, QueuePosition)`).
- Economy delta: `CurrencyLedgerEntry.EntryType` (K.3) gains `spend_media` (entry cost) and `refund_media` (rejected/skipped refund) — owner `economy.md`.

**Auto-mod escalation ladder added (owner `moderation.md`):**
- `ModerationEscalationPolicies` (J.10, soft-delete) added beside the moderation siblings — per-channel escalation-ladder config (`IsEnabled` opt-in; `LadderJson` [VC:JSON] `List<EscalationLadderStep>` offense-count→action, empty seeds the safety-baseline default 1→warn/2→60s/3→600s/4→3600s/5→86400s/6+→ban; `OffenseWindowHours` default 168; `CountAutoModViolations` default false; `ConfigSchemaVersion`; **Unique** `BroadcasterId`, one per channel). The **explicit discrete** escalation path, complementary to the **continuous** heat path (J.5 `HeatScore` + J.7 `AutoTimeoutOnHeat`/`HeatTimeoutThreshold`).
- `ModerationEscalationStates` (J.11, mutable — not append-only) added — per-subject offense tally (`SubjectUserId` FK→`Users`, `SubjectTwitchUserId` [PII-hash]; `OffenseCount`; `WindowStartedAt`/`LastOffenseAt`; **Unique** `(BroadcasterId, SubjectUserId)`); cleared by the forgiveness reset.
- `ChatFilters.Action` enum (J.6) extended with `escalate` — a filter with `Action=escalate` defers the action to the J.10 ladder (`ChatFilterAction { Delete, Timeout, Hold, Flag, Escalate }`).

**Supporter Events domain — generalizes & supersedes the old Donations domain (owner `supporter-events.md`, supersedes `donations.md`):**
- **P.15 `DonationConnections` → `SupporterConnections`** (soft-delete, beside `ObsConnections` P.14) — renamed and broadened from per-channel tip-ingest to generic per-channel+source monetization-ingest config. `SourceKey string(30)` [VC:enum] now spans `streamelements`\|`streamlabs`\|`kofi`\|`patreon`\|`fourthwall`\|`tipeee`\|`treatstream`\|`donordrive`\|`pally`\|`shopify`; `ConnectionMode string(20)` [VC:enum] `webhook`\|`socket`\|`ws`\|`poll`; `AuthSecretCipher` AEAD secret [PII-shred] via `IFieldCipher` (null when OAuth-vaulted); **added** `IntegrationConnectionId` FK→`IntegrationConnections` Null (OAuth providers — Patreon/Shopify/TreatStream); `InboundWebhookEndpointId` FK→`InboundWebhookEndpoints` (H.10) for webhook providers; `IsEnabled`/`Status`/`LastEventAt`/`ConfigSchemaVersion`; **Unique** `(BroadcasterId, SourceKey)`. Dropped the SE-only `SeTransport`/`Mode` columns into the generalized `ConnectionMode`.
- **P.16 `DonationRecords` → `SupporterEvents`** (APPEND-ONLY) — renamed and broadened. **Added** `Kind string(20)` [VC:enum] `tip`\|`membership`\|`merch`\|`charity`, `Tier string(50) Null` (membership tier), `Quantity int Null` (months / item count), `ItemsJson text Null` [VC:JSON] (merch line-items), `IsRecurring bool`, `PayloadJson text` [VC:JSON] (normalized raw). Renamed `DonorName`→`SupporterDisplayName` [PII-scrub], `DonorUserId`→`SupporterUserId` FK→`Users` Null, `Amount decimal(12,2)`→`AmountMinor long Null` (minor units), `Message`→`MessageText` [PII-scrub]; dropped `IsPublic`. `ProviderTransactionId string(120)` (provider id or composite hash where none exists). **Unique** `(BroadcasterId, SourceKey, ProviderTransactionId)`; **Index** `(BroadcasterId, ReceivedAt)`, `(BroadcasterId, Kind)`. Backs the `supporter.tip`/`supporter.membership`/`supporter.merch`/`supporter.charity`/`supporter.any` pipeline triggers (the old `donation` trigger becomes `supporter.tip`) + Alerts widget (branches on `Kind`).
- H.10 `InboundWebhookEndpoint.AdapterKind` value `kofi` → `supporter` — the generic supporter webhook adapter dispatches by `SourceKey` (Ko-fi/Patreon/Fourthwall/Shopify); Ko-fi is now `SourceKey=kofi` under it.

**External Automation API added (owner `automation-api.md`):**
- `AutomationApiToken` (P.17, soft-delete) added beside `SupporterEvents` (P.16) — per-channel scoped credential for the plain-WebSocket/REST automation surface (`TokenHash string(64)` **Unique** SHA-256 of the secret, never stored plaintext, the `RefreshToken` pattern; `TokenPrefix string(16)` non-secret display id; `ScopesJson` [VC:JSON] `string[]` ⊆ `invoke`\|`read`\|`events`\|`chat`, default-deny; `AllowedPipelineIdsJson text Null` [VC:JSON] `Guid[]` invoke-allowlist; `LastUsedAt`/`ExpiresAt`/`RevokedAt`; `CreatedByUserId` FK→`Users`; **Unique** `(BroadcasterId, Name)`; **Index** `TokenPrefix`). Secret shown once on creation; lifecycle journaled (Critical-tier credential).

**Sound System domain added (owner `sound-system.md`):**
- `SoundClip` (P.18, soft-delete) added beside `AutomationApiToken` (P.17) — per-channel curated sound-clip library (`Name string(50)` slug used by `play_sound`; `DisplayName string(100)`; `StorageKey string(200)` key in `ISoundClipStore`; `MimeType string(40)` `audio/mpeg`\|`audio/ogg`\|`audio/wav`; `DurationMs int`; `SizeBytes long`; `DefaultVolume int` 0–100 default 80; `IsEnabled bool` default true; `CreatedByUserId` FK→`Users`; **Unique** `(BroadcasterId, Name)`; **Index** `BroadcasterId`). Audio blob in the durable deployment-profile `ISoundClipStore`, played on the overlay audio bus via the new `IOverlayClient.PlaySound`; no play-log table (a play is a transient overlay push, already journaled by the pipeline run). Pipeline delta: `commands-pipelines.md` gains `play_sound`/`stop_sound` actions.

**Pronoun provider integration added (owner `pronouns.md`):**
- `Users.AltPronounId` (A.1) `guid FK→Pronouns Null Index` **[PII-S9]** added beside `PronounId`/`PronounManualOverride` — secondary pronoun (alejo `alt_pronoun_id`) driving the `{{user.pronouns}}` display badge's second half; FK + Index added to the A.1 footer; nulled on erasure with `PronounId` (§5 [PII-S9] scrub list + erasure step 1). No new table — the per-viewer value lives on `Users`, lookups cache in `ICacheService` (`commands-pipelines.md` gains the `{{user.pronouns}}`/`{{target.pronouns}}` helper).

**Bot-side moderation standing added (owner `moderation.md`, 2026-07-17):**
- `ChannelModerationStanding` (J.12) added beside the escalation siblings (J.10/J.11) — the negative bot-side moderation axis (graduated ignore tiers `muted`\|`shadowbanned`\|`blacklisted`; absent row = normal); raw platform `UserId` (deliberately **not** [PII-hash] — hot-path equality match on live inbound chat ids + operator panel); **Unique** `(BroadcasterId, Provider, UserId)`, **Index** `(BroadcasterId, Standing)` (`moderation.md` §1 J.12 + §9 decision 3).

**Pipeline control-flow tree added (owner `pipeline-control-flow.md`):**
- `PipelineSteps` (H.2) extended with two columns (no new table) — `BlockKind string(20) Null` [VC:enum] (`switch`\|`switch_case`\|`loop`\|`random_branch`\|`random_case`; null = leaf action step) + `BlockConfigJson text Null` [VC:JSON] (block params: switch value/case-match + comparison; loop mode/list-var/count/while-condition; case weight). The existing `ParentStepId` self-FK→`PipelineSteps.Id` + its Index now also carry block nesting; `Order` is reframed as **order-within-parent** and the step set forms a **tree** (block steps own ordered children, walked depth-first under iteration/recursion/total-action/runtime caps). New pipeline actions `run_pipeline`/`break`/`continue` + the block-kinds are config-only (no new role keys; `PipelineStep` stays tenant-scoped via `Pipeline`).

**Discord personal live-notification DMs added (owner `2026-06-16-discord-notifications.md`, 2026-07-17):**
- `DiscordNotificationRole.DmEnabled bool` (P.10, default false — this role also DMs its opted-in members on dispatch) + `DiscordMemberOptIn.DmChannelId string(32) Null` (P.10 — cached Discord DM channel snowflake, set on first DM) added; per-member DMs reuse `DiscordNotificationDispatch` with `DedupeKey = "{baseDedupeKey}:dm:{discordMemberId}"` (no new table).

---

# NomNomzBot — Unified Database Schema (PostgreSQL + SQLite)

**Status:** Authoritative consolidation of all five domain table sets (Identity/Access/Federation, Economy/Engagement/Analytics, Content/Commands/Moderation, Integrations/Media/Widgets, Platform/Compliance/Billing) into ONE coherent, normalized, complete model. Designed in full so no add-a-column / add-an-FK / remove-this / default-that migration is ever needed.

This spec resolves every inter-set inconsistency, dedupes every overlap, and guarantees every FK targets an existing surrogate key.

---

## 1. CONVENTIONS (apply to every table; stated once, never repeated per row)

### 1.1 Keys — **LOCKED**
- **Surrogate PK `Id` on every table.** Type is `guid` generated **app-side as UUIDv7** via native `Guid.CreateVersion7()` (.NET 9+; this build is .NET 10 / C# 14 / EF Core 10) — time-ordered / index-friendly like a ULID, **zero 3rd-party lib**. Never DB-default-generated, never `Guid.NewGuid()` for new rows. Stored portably — `uuid` on Postgres, `TEXT`/`char(36)` on SQLite via EF. **Exception:** append-only high-volume journals/logs/snapshots use `bigint` identity for monotonic ordering. The PK is **never** PII and is the **only** thing FKs reference.
- **External provider ids are first-class indexed attributes, never keys.** `TwitchUserId`, `TwitchChannelId`, `TwitchRewardId`, `TwitchRedemptionId`, `GuildId`, `StripeSubscriptionId`, etc. are stored as plain indexed columns (unique-indexed where they must dedupe) — fully usable consumer-side and for every Helix call. Anonymizing one never touches the FK graph; the guid is internal FK + GDPR-shred only.
- **All FKs reference surrogate `Id` columns** (`Users.Id`, `Channels.Id`, …). No FK ever points at a Twitch id.
- **Tenant key `BroadcasterId` is `guid`** (FK→`Channels.Id`) everywhere — `ITenantScoped.BroadcasterId` is widened `string`→`Guid`. One-time clean-slate rebuild; enables O(1) cascade-safe erasure. **DECIDED — adopt (owner decision #1).**

> **Load-bearing rebuild decision (resolves the cross-set conflict).** The five inputs disagreed on `BroadcasterId` type (`string(50)` in three, `guid`/`long` in two) and on whether `Users.Id`/`Channels.Id` stay raw-Twitch-id PKs. **This unified model standardizes on `guid` surrogate keys everywhere, and `BroadcasterId` is `guid` (FK→`Channels.Id`).** That requires widening the existing `ITenantScoped.BroadcasterId` from `string` to `Guid` and demoting today's raw-Twitch-id PKs (`User.Id`, `Channel.Id`, `string(50)`) to indexed attribute columns (`TwitchUserId`, `TwitchChannelId`). This is the single deliberate, one-time rebuild change the GDPR doc already targets — it is the precondition for O(1), cascade-safe erasure and must be done now, not migrated to later.

### 1.2 Tenant scoping
- **`BroadcasterId guid` (FK→`Channels.Id`)** on every tenant-owned table → implements `ITenantScoped`, gets the EF global query filter **and** (Postgres/SaaS) RLS via `SET app.tenant_id`. SQLite/self-host relies on the EF filter alone. Child rows carry a **denormalized** `BroadcasterId` even when reachable via parent, so RLS/filter applies without a join.
- **Global (non-tenant) tables are the deliberate exception** and carry NO `BroadcasterId`: all `Iam*`, all `Federation*`, `ActionDefinitions`, `IamPermissions/Roles/...`, `BillingTier`, `TierLimit`, `FeatureFlag`, `DeploymentProfile`, `TtsVoice`, `TtsCacheEntry`, `WidgetGalleryItem`, `WidgetGallerySubmissionEvent`, platform-scope `CryptoKey`. Cross-tenant tables (shared jars, federation) are guarded by membership/trust predicates instead of single-tenant RLS.

### 1.3 Timestamps & soft-delete
- `CreatedAt` + `UpdatedAt` (UTC) on every mutable table (from `BaseEntity`).
- `DeletedAt` (nullable, UTC) where soft-delete applies (from `SoftDeletableEntity`). Never hard-`DELETE`.
- **Append-only tables** (journals, ledgers, audit logs, immutable snapshots, usage records) carry **`CreatedAt` only — no `UpdatedAt`, no `DeletedAt`.** Corrections are reversing rows, never edits. Marked **[APPEND-ONLY]**.

### 1.4 Provider-agnostic types + value-converter notes — **LOCKED**
Only these portable types appear: `guid`, `bigint`, `int`, `bool`, `text`, `string(n)` (`[MaxLength(n)]`), `timestamp` (`DateTime` UTC), `date` (`DateOnly`), `decimal(p,s)`, `blob` (`byte[]`).
- **No `jsonb`, no native arrays, no `citext`.** Any `List<>` / `Dictionary<>` / object / enum-as-text column is portable `text` via an **EF `ValueConverter`** (+`ValueComparer` for collections), serialized with **Newtonsoft.Json**. Flagged **[VC:JSON]** (collection/object) or **[VC:enum]** (enum↔short text/int).
- **Net-new converter work — NOT "already done".** The live code is the **anti-pattern this spec bans**: `ChannelConfiguration.cs` uses `.HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")` (Postgres-native, SQLite-breaking). Every `[VC:JSON]` column is converter work to do now; `HasColumnType("jsonb")` and `HasDefaultValueSql("…::jsonb")` are **banned** by a config-review gate.
- **SQLite provider must be wired.** Today `DependencyInjection.cs` wires `UseNpgsql` **only**. The deployment-profile adapter (`DeploymentProfile.DbProvider` = `postgres`\|`sqlite`) selects `Microsoft.EntityFrameworkCore.Sqlite` vs Npgsql via DI. A SQLite migration is generated in CI as a **provider-parity test** before "ship-ready".
- **Per-tenant monotonic `bigint` is app-assigned, not a DB sequence.** PG `IDENTITY`/sequences and SQLite `AUTOINCREMENT` are **global**, and SQLite has no sequences. The per-tenant monotonic value (`EventJournal.StreamPosition`, `CurrencyLedgerEntries` ordering) is **application-computed under a per-tenant lock**: a dedicated `TenantSequences(BroadcasterId, SequenceName, NextValue)` row (see Q.3) is read-incremented in the **same transaction** as the insert, serialized per `(BroadcasterId, SequenceName)` by a row lock (`SELECT … FOR UPDATE` on PG; `BEGIN IMMEDIATE` write-lock on SQLite). No DB auto-increment is relied on for per-tenant ordering.
- `guid`→text and `decimal`→native both work unchanged on PG + SQLite, but **indexed `decimal` sort is REAL-affinity on SQLite** (`UserTrustScores.TrustScore/HeatScore`, `SongRequestQueues.MinYouTubeTrustScore`): stored as `decimal(8,4)`, accepted as REAL affinity for ORDER BY/range on SQLite (documented lossy compare; scale to basis-point `int` only if exact ordering is later required).
- **Case-insensitive uniqueness uses an explicit `*Normalized` lowercase column + unique index** (no `citext`, no `LOWER()` expression index). Added on `Users.Username`→`UsernameNormalized`, `Channels.Name`→`NameNormalized`, `Commands.Name`→`NameNormalized`, `CatalogItems.Name`→`NameNormalized`; the unique constraint references the `*Normalized` column.
- **Two migration sets** (Postgres + SQLite) generate from this one model.

### 1.5 PII flagging (for crypto-shred / anonymization)
Columns are flagged in Notes:
- **[PII-hash]** — external identifier (Twitch/Discord user id). On erasure: replaced in place by a **consistent deterministic hash** everywhere at once (FK graph untouched).
- **[PII-scrub]** — human content/identifier (username, display name, message body, free-text reason, note). On erasure: nulled/tombstoned.
- **[PII-shred]** — secret/sensitive encrypted blob (OAuth token, email, IP, billing email, API key). Stored as ciphertext under a per-tenant/per-subject **DEK**; erasure destroys the DEK (`CryptoKey.Status=destroyed`) → ciphertext permanently unreadable (O(1), backups included).
- **[PII-S9]** — special-category (GDPR Art. 9, e.g. pronoun). Extra-care + explicit-consent gated.
- The surrogate `Id` is **never** PII.

---

## DOMAIN A — Identity, Tenancy & Sessions

### A.1 Users `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate; FK target everywhere; survives anonymization. |
| TwitchUserId | string(50) | Unique, Index, Null | External id (attribute). **[PII-hash]**. |
| Platform | string(20) | Index | `twitch`\|`kick`\|`youtube`. Default `twitch`. [VC:enum]. |
| Username | string(255) | Index | **[PII-scrub]**. |
| UsernameNormalized | string(255) | Unique, Index | Lowercased `Username` for case-insensitive uniqueness (no `citext`). Nulled with `Username` on scrub → uniqueness drops with the identity. |
| DisplayName | string(255) | Null | **[PII-scrub]**. |
| NickName | string(255) | Null | **[PII-scrub]**. |
| EmailCipher | string(512) | Null | **[PII-shred]** encrypted; DEK via `SubjectKeyId`. |
| SubjectKeyId | guid | FK→CryptoKey, Null, Index | Per-subject DEK (email + event PII). |
| ProfileImageUrl | string(2048) | Null | |
| OfflineImageUrl | string(2048) | Null | |
| Color | string(7) | Null | `#RRGGBB`. |
| BroadcasterType | string(50) | | `""`\|`affiliate`\|`partner`. |
| PronounId | guid | FK→Pronouns, Null, Index | **[PII-S9]** lookup FK (NOT an enum — grammar attrs subject/object/… drive TTS/pronunciation). Special-category; explicit-consent gated. Nulled on scrub. |
| AltPronounId | guid | FK→Pronouns, Null, Index | **[PII-S9]** secondary pronoun (alejo `alt_pronoun_id`); drives the display badge's second half only. Special-category; nulled on scrub with `PronounId`. |
| PronounManualOverride | bool | | |
| Timezone | string(50) | Null | |
| Description | string(500) | Null | |
| IsPlatformPrincipal | bool | Index | True if also a Plane-C IAM principal (links via `IamPrincipals.UserId`). Replaces the old `User.IsAdmin` bool. |
| IsBot | bool | Index | Marks a viewer identity that is itself a bot account (distinct from `BotAccounts`). |
| LastSeenAt | timestamp | Null, Index | Last time this identity was observed globally. |
| IsAnonymized | bool | Index | Set after erasure. |
| Enabled | bool | | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**PK** `Id`. **FK** `SubjectKeyId`→`CryptoKey.Id`, `PronounId`→`Pronouns.Id`, `AltPronounId`→`Pronouns.Id`. **Indexes** `TwitchUserId`(unique), `UsernameNormalized`(unique), `Platform`, `Username`, `PronounId`, `AltPronounId`, `IsPlatformPrincipal`, `IsBot`, `IsAnonymized`, `LastSeenAt`.
Purpose: every distinct platform identity (streamers, mods, viewers) — the surrogate-keyed subject all access/grants/moderation/economy rows reference.

### A.2 Channels `[soft-delete]` — the tenant root
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | = the tenant id used in every `BroadcasterId` + RLS. |
| OwnerUserId | guid | FK→Users, Unique, Index | Broadcaster identity; one channel per owner. |
| TwitchChannelId | string(50) | Unique, Index | **[PII-hash]** (identifies broadcaster). |
| Platform | string(20) | Index | `twitch`\|`kick`\|`youtube`. [VC:enum]. |
| Name | string(25) | Index | **[PII-scrub]** channel/login name. |
| NameNormalized | string(25) | Unique, Index | Lowercased `Name` for case-insensitive uniqueness (no `citext`). |
| Status | string(20) | Index | Tenant lifecycle: `active`\|`suspended`\|`churned`\|`platform_banned`. [VC:enum]. Target of Plane-C `tenant:suspend`. |
| SuspendedAt | timestamp | Null | Set when `Status=suspended`/`platform_banned`. |
| SuspendedReason | string(500) | Null | Justification (ToS / churn note). |
| DeploymentMode | string(20) | | `saas`\|`self_host_lite`\|`self_host_full`. [VC:enum]. |
| BillingTierKey | string(20) | Index | Denormalized current tier (`free`\|`base`\|`pro`\|`premium`); source of truth is `Subscriptions`. |
| IsFounder | bool | | Cosmetic perk (also tracked in `FoundersBadge`). |
| IsOnboarded | bool | | |
| Enabled | bool | | |
| IsLive | bool | | |
| OverlayToken | string(36) | Unique | Opaque browser-source token (not PII). |
| SongRequestPageToken | string(64) | Null, Unique | Opaque, rotatable public song-request page token (not PII; mirrors `OverlayToken`). Resolves the `/(public)/sr/[channel]` page → `BroadcasterId`; null until first minted. See `music-sr.md` §3.7. |
| DefaultCommandPrefix | string(8) | | Channel-level default command prefix; default `!`. The effective prefix for any command whose `Commands.PrefixMode=Default`. Surfaced as `{{bot.prefix}}`. |
| ShoutoutTemplate | string(450) | Null | (carried from current entity). |
| LastShoutout | timestamp | Null | |
| ShoutoutInterval | int | | Default 10. |
| UsernamePronunciation | string(100) | Null | |
| BotJoinedAt | timestamp | Null | |
| StreamDelay | int | | |
| Language | string(50) | Null | |
| GameId | string(50) | Null | |
| GameName | string(255) | Null | |
| Title | string(255) | Null | |
| Tags | text | Null | **[VC:JSON]** `List<string>`. |
| ContentLabels | text | Null | **[VC:JSON]** `List<string>`. |
| IsBrandedContent | bool | | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**PK** `Id`. **FK** `OwnerUserId`→`Users.Id`. **Indexes** `OwnerUserId`(unique), `TwitchChannelId`(unique), `NameNormalized`(unique), `Platform`, `BillingTierKey`, `Status`.
Purpose: the tenant root — one row per managed channel; its `Id` scopes every tenant-owned table.

### A.3 AuthSessions
| Name | Type | Key/Null/Index | Notes |
|---|---|---|---|
| Id | guid | PK | Session id. |
| UserId | guid | FK→Users, Index | Authenticated principal. |
| BroadcasterId | guid | FK→Channels, Null, Index | Active tenant context (resolved from JWT `sub`). |
| ClientType | string(20) | Index | `web`\|`desktop`\|`mobile`\|`ipc_dev`. [VC:enum]. |
| IpAddressCipher | string(255) | Null | **[PII-shred]** hashed/encrypted, retention-bound. |
| UserAgent | string(512) | Null | **[PII-scrub]** device fingerprint. |
| CreatedAt | timestamp | | |
| LastSeenAt | timestamp | Index | |
| ExpiresAt | timestamp | Index | |
| RevokedAt | timestamp | Null | Logout/admin/erasure. |
| UpdatedAt | timestamp | | |

**PK** `Id`. **FK** `UserId`→`Users.Id`, `BroadcasterId`→`Channels.Id`.
Purpose: live login session per device, carrying resolved tenant context — parent of refresh tokens, revocable on logout/erasure.

### A.4 RefreshTokens
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| SessionId | guid | FK→AuthSessions, Index | Owning session. |
| UserId | guid | FK→Users, Index | Denormalized for revoke-all-by-user (erasure). |
| TokenHash | string(64) | Unique, Index | Hashed token; never plaintext. |
| PreviousTokenHash | string(64) | Null, Index | Rotation lineage / replay detection. |
| IssuedAt | timestamp | | |
| ExpiresAt | timestamp | Index | |
| ConsumedAt | timestamp | Null | Single-use rotation. |
| RevokedAt | timestamp | Null | |
| RevokedReason | string(30) | Null | `logout`\|`rotation`\|`reuse_detected`\|`erasure`\|`admin`. [VC:enum]. |
| CreatedAt/UpdatedAt | timestamp | | |

**PK** `Id`. **FK** `SessionId`→`AuthSessions.Id`, `UserId`→`Users.Id`. **Unique** `TokenHash`. **Index** `(UserId, RevokedAt)` (revoke-all-by-user on erasure skips already-revoked rows).
Purpose: hashed, single-use, rotating refresh tokens with replay detection and bulk-revoke-by-user.

### A.5 IpcDevModeKeys `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| KeyHash | string(64) | Unique, Index | Hashed local-IPC gate key. |
| Label | string(100) | Null | |
| IsEnabled | bool | Index | Off by default; local socket only. |
| CreatedByUserId | guid | FK→Users, Null | |
| ExpiresAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

Purpose: opt-in, key-gated credential for the local developer-mode IPC socket (hashed, disabled by default, never remote).

---

## DOMAIN B — Access: Ladders, Per-Action Permissions, Permits

### B.1 ChannelMemberships `[soft-delete]` — management ladder
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| UserId | guid | FK→Users, Index | Member. |
| ManagementRole | string(20) | Index | `moderator`\|`super_mod`\|`editor`\|`broadcaster`. [VC:enum]. |
| LevelValue | int | Index | Denormalized 10/20/30/40 for fast gate. |
| Source | string(20) | | `twitch_badge`\|`helix_editors`\|`bot_grant`\|`owner`. [VC:enum]. |
| GrantedAt | timestamp | | |
| GrantedByUserId | guid | FK→Users, Null | Actor (no-escalation check). |
| LastSyncedAt | timestamp | Null | Helix reconcile. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, UserId)`. Purpose: who administers a channel and at what management level (mods/super-mods/editors/owner under one ladder).

### B.2 ChannelCommunityStandings
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| UserId | guid | FK→Users, Index | Viewer. |
| Standing | string(20) | Index | `everyone`\|`subscriber`\|`vip`\|`artist`\|`moderator`. [VC:enum]. |
| LevelValue | int | Index | Numeric community level. |
| Source | string(20) | | `chat_tags`\|`eventsub_badge`. [VC:enum]. |
| SubTier | string(8) | Null | `1000`\|`2000`\|`3000`. |
| LastSeenAt | timestamp | Null | |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(BroadcasterId, UserId)`. Purpose: per-channel community standing (chat-badge axis) for command/cosmetic gating — distinct from the management ladder.

### B.3 ActionDefinitions `[GLOBAL, seed]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| ActionKey | string(100) | Unique, Index | e.g. `channel:title:write`, `moderation:ban`, `permit:issue`. |
| Plane | string(20) | | `community`\|`management`. [VC:enum]. |
| DefaultLevel | int | | Ships ≥ floor. |
| FloorLevel | int | | Broadcaster cannot set below this. |
| FloorTier | string(20) | | `critical`\|`tos`\|`low`. [VC:enum]. |
| IsGrantableViaPermit | bool | | False for `critical`. |
| Description | string(500) | Null | UI copy. |
| CreatedAt/UpdatedAt | timestamp | | |

Purpose: global catalog of gated actions with default/floor/danger-tier/permit-eligibility; the resolver + permissions UI read it.

### B.4 ChannelActionOverrides `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| ActionDefinitionId | guid | FK→ActionDefinitions, Index | |
| OverrideLevel | int | | Resolver clamps `clamp(override ?? default, floor, Broadcaster)`. |
| SetByUserId | guid | FK→Users, Null | Audit. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, ActionDefinitionId)`. Purpose: per-channel raise/lower of an action's required level, floor-clamped.

### B.5 PermitGrants `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| UserId | guid | FK→Users, Index | Granted individual. |
| GrantType | string(20) | | `role`\|`capability`. [VC:enum]. |
| GrantedRole | string(20) | Null | When `role` (never above grantor, never critical). [VC:enum]. |
| ActionDefinitionId | guid | FK→ActionDefinitions, Null | When `capability`. |
| GrantedByUserId | guid | FK→Users, Index | Grantor (no-escalation guardrail). |
| ExpiresAt | timestamp | Null | Optional time-box. |
| RevokedAt | timestamp | Null | `!unpermit`/expiry. |
| Reason | string(500) | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Index** `(BroadcasterId, UserId)`. Purpose: individual `!permit` grants decoupled from Twitch badges; enforces both guardrails + optional expiry.

> **Replaces** the current generic `Permission` (`SubjectType/ResourceType/PermissionValue`) → split into B.3+B.4+B.5.

---

## DOMAIN C — Platform IAM (Plane C, GLOBAL, default-deny)

> Not tenant-scoped, not a channel-ladder rung. Replaces the coarse `User.IsAdmin`. Self-host collapses to "owner = full" (tables empty).

### C.1 IamPermissions `[GLOBAL, seed]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| Key | string(60) | Unique, Index | `tenant:read`,`tenant:suspend`,`tenant:access`,`billing:read`,`billing:refund`,`featureflag:write`,`audit:read`,`iam:manage`. |
| Category | string(20) | | `tenant`\|`billing`\|`audit`\|`iam`\|`featureflag`. [VC:enum]. |
| IsSensitive | bool | | Break-glass/cross-tenant. |
| Description | string(500) | Null | |
| CreatedAt/UpdatedAt | timestamp | | |

### C.2 IamRoles `[GLOBAL, soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| Name | string(40) | Unique, Index | `Support-Agent`,`Billing-Admin`,`Read-Only-Auditor`,`On-Call-Engineer`,`IAM-Admin`. |
| IsSystem | bool | | |
| Description | string(500) | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

### C.3 IamRolePermissions
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| RoleId | guid | FK→IamRoles, Index | |
| PermissionId | guid | FK→IamPermissions, Index | |
| CreatedAt | timestamp | | |

**Unique** `(RoleId, PermissionId)`. Purpose: M2M bundling of permissions into roles.

### C.4 IamPrincipals `[GLOBAL, soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| PrincipalType | string(20) | Index | `employee`\|`service_account`. [VC:enum]. |
| UserId | guid | FK→Users, Null, Index | Set for employees. |
| Name | string(100) | Index | **[PII-scrub]** for employees. |
| EmailCipher | string(512) | Null | **[PII-shred]** employees only. |
| SubjectKeyId | guid | FK→CryptoKey, Null | DEK for email. |
| ServiceAccountKeyHash | string(128) | Null | Hashed machine secret. |
| IsActive | bool | Index | |
| ExpiresAt | timestamp | Null | Time-boxed/break-glass identities. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

Purpose: SaaS operators (employees + machine service accounts) IAM roles attach to. (Consolidates the two `IamPrincipal` shapes the inputs proposed.)

### C.5 IamRoleAssignments
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| PrincipalId | guid | FK→IamPrincipals, Index | |
| RoleId | guid | FK→IamRoles, Index | |
| ScopeChannelId | guid | FK→Channels, Null, Index | Narrow to ONE tenant; null = platform-wide. |
| AssignedByPrincipalId | guid | FK→IamPrincipals, Null | Audit. |
| ExpiresAt | timestamp | Null | Break-glass time-box. |
| RevokedAt | timestamp | Null | |
| Reason | string(500) | Null | Justification. |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(PrincipalId, RoleId, ScopeChannelId)`. Purpose: binds principals to roles, optionally tenant-narrowed and time-boxed.

---

## DOMAIN D — Federation (GLOBAL, cross-instance trust)

### D.1 FederationPeers `[GLOBAL, soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| InstanceId | string(36) | Unique, Index | Peer deployment id. |
| DisplayName | string(100) | Null | |
| BaseUrl | string(2048) | Null | Peer endpoint/bus. |
| DeploymentMode | string(20) | | `saas`\|`self_host_lite`\|`self_host_full`. [VC:enum]. |
| TrustState | string(20) | Index | `pending`\|`trusted`\|`revoked`\|`blocked`. [VC:enum]. |
| FirstSeenAt | timestamp | | |
| LastHandshakeAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

### D.2 FederationPeerKeys
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| PeerId | guid | FK→FederationPeers, Index | |
| PublicKey | text | | PEM/base64. |
| Algorithm | string(30) | | `ed25519`\|`rsa-sha256`. |
| KeyId | string(64) | Index | Rotation identifier. |
| ValidFrom | timestamp | | |
| ValidTo | timestamp | Null | |
| IsActive | bool | Index | |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(PeerId, KeyId)`. Purpose: public keys verifying signed federation events, with rotation.

### D.3 ChannelFederationOptIns `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant (opt-in is per channel). |
| PeerId | guid | FK→FederationPeers, Null, Index | Specific peer or null = any trusted. |
| OptInType | string(30) | Index | `shared_chat_bans`\|`shared_ban_list`\|`shared_trust_list`\|`shared_savings`. [VC:enum]. |
| Direction | string(10) | | `accept`\|`share`\|`both`. [VC:enum]. |
| IsEnabled | bool | Index | |
| EnabledByUserId | guid | FK→Users, Null | Audit. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, PeerId, OptInType)`. Purpose: per-channel, per-type federation opt-ins (default-deny, super-mod/broadcaster gated, reversible).

---

## DOMAIN E — Integration Connections, Tokens & Bot Accounts

> **Replaces** the flat-token `Service` entity. Tokens move to a per-provider, crypto-shred-ready vault keyed off a DEK. **Single canonical connection table** (resolves the duplicate `IntegrationConnection` proposed by the Identity and Integrations sets).

### E.1 IntegrationConnections `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Null, Index | Null = platform/global connection (shared bot app creds). |
| Provider | string(20) | Index | Registry key, e.g. `twitch`\|`spotify`\|`discord`\|`youtube`\|`azure_tts`\|`elevenlabs`\|… — open key (music providers self-register via `IMusicProviderRegistry`), NOT a closed enum. |
| ProviderAccountId | string(255) | Null, Index | **[PII-hash]** external account id. |
| ProviderAccountName | string(255) | Null | **[PII-scrub]**. |
| Status | string(20) | Index | `connected`\|`expired`\|`revoked`\|`needs_reauth`\|`pending`. [VC:enum]. |
| Scopes | text | Null | **[VC:JSON]** granted scopes. |
| ClientId | string(512) | Null | Non-secret app id. |
| IsByok | bool | | Bring-your-own-key vs platform-managed. |
| Settings | text | Null | **[VC:JSON]** provider-agnostic config (market/region). |
| ConnectedByUserId | guid | FK→Users, Null | |
| ConnectedAt | timestamp | Null | |
| LastRefreshedAt | timestamp | Null | Last successful token refresh (drives proactive refresh). |
| LastErrorAt | timestamp | Null | Last refresh/use failure (drives backoff). |
| ConsecutiveFailureCount | int | | Resets on success; backoff/needs-reauth driver. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Provider, ProviderAccountId)`. Purpose: one row per (channel/global, provider) connection — parent of encrypted tokens; provider-specific config lives in E.4 / E.5 `MusicProviderConfig` / integration tables.

### E.2 IntegrationTokens `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| ConnectionId | guid | FK→IntegrationConnections, Index | |
| BroadcasterId | guid | FK→Channels, Null, Index | Denormalized for RLS. |
| TokenType | string(10) | Index | `access`\|`refresh`\|`app`. [VC:enum]. |
| CipherText | text | | **[PII-shred]** AES token (base64). |
| Nonce | string(64) | Null | AEAD IV. |
| EncryptionKeyId | guid | FK→CryptoKey, Index | DEK reference; destroy → shreds all tokens under it. |
| ExpiresAt | timestamp | Null | |
| RotatedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(ConnectionId, TokenType)`. Purpose: encrypted OAuth secrets, per-type, tied to a per-tenant DEK for O(1) crypto-shred.

### E.3 BotAccounts `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| IdentityType | string(10) | Index | `shared`\|`custom`. [VC:enum]. |
| Platform | string(20) | Index | `twitch`\|`kick`\|`youtube`. [VC:enum]. |
| BotUserId | string(50) | Unique, Index | **[PII-hash]** external bot id. |
| BotUsername | string(255) | Index | **[PII-scrub]**. |
| ConnectionId | guid | FK→IntegrationConnections, Null, Index | Bot OAuth tokens in the vault. |
| IsActive | bool | Index | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Index** `(Platform, IdentityType)`. Purpose: bot identities (one shared + optional per-channel custom) driving IRC/EventSub.

### E.4 ChannelBotAuthorizations `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Was `int`; now surrogate guid. |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| BotAccountId | guid | FK→BotAccounts, Index | |
| AuthorizedAt | timestamp | | |
| AuthorizedByUserId | guid | FK→Users, Null | |
| BotJoinedAt | timestamp | Null | First-run join. |
| IsActive | bool | Index | `needs_reauth` after DEK rotation. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, BotAccountId)`. Purpose: which bot identity speaks for the tenant in chat (supersedes the int-keyed `ChannelBotAuthorization`).

### E.5 MusicProviderConfig `[soft-delete]` — generic per-provider SR/playback config
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant (denormalized for RLS). `ITenantScoped`. |
| Provider | string(30) | Index | Registry key (`spotify`\|`youtube`\|…) — open `IMusicProviderRegistry` key, NOT a closed enum. |
| ConnectionId | guid | FK→IntegrationConnections, Unique | The provider's connected E.1 row (1:1). |
| AllowSongRequests | bool | | Shared gate: this provider may feed the SR queue. |
| MaxQueueLength | int | | Shared cap. |
| BlockExplicit | bool | | Shared explicit-content gate. |
| ProviderSettings | text | Null | **[VC:JSON]** provider-specific knobs (Spotify: `Market`/`RequirePlaylistContext`/`FallbackPlaylistUri`; YouTube: `Region`/`MaxVideoDurationSeconds`/`BlockAgeRestricted`/`EmbeddableOnly`; other registered providers: their own). Shape validated by the provider's registered settings schema. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Provider)`. Purpose: one config row per (channel, provider) with provider-specific rules in a `[VC:JSON]` blob — adding a provider needs no new table/migration. Replaces the former per-provider `SpotifyIntegrationConfig`/`YouTubeIntegrationConfig` (E.5/E.6), collapsing both into this generic row.

---

## DOMAIN F — Twitch Domain (Streams, Subs, Followers, Events, Rewards, EventSub)

### F.1 Streams `[soft-delete]` — live broadcast session
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| TwitchStreamId | string(50) | Index | External attr. |
| Type | string(20) | | `live`. |
| Title | string(255) | Null | |
| GameId | string(50) | Null, Index | |
| GameName | string(255) | Null | |
| Language | string(8) | Null | |
| Tags | text | Null | **[VC:JSON]**. |
| ContentLabels | text | Null | **[VC:JSON]**. |
| IsMature | bool | | |
| ViewerCountPeak | int | Null | |
| ThumbnailUrl | string(2048) | Null | |
| StartedAt | timestamp | Index | `stream.online`. |
| EndedAt | timestamp | Null | `stream.offline`; null = live. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

Purpose: one row per live broadcast; anchors session-scoped analytics (chat, redemptions, follows). (Renames/extends the thin existing `Stream`; referenced as `Streams` throughout.)

### F.2 TwitchSubscribers `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| SubscriberUserId | guid | FK→Users, Index | Internal surrogate of the viewer. |
| SubscriberTwitchUserId | string(50) | Index | **[PII-hash]**. |
| SubscriberDisplayNameSnapshot | string(255) | Null | **[PII-scrub]**. |
| Tier | string(8) | | `1000`\|`2000`\|`3000`. |
| IsGift | bool | | |
| GifterUserId | guid | FK→Users, Null | Internal surrogate. |
| CumulativeMonths | int | Null | |
| StreakMonths | int | Null | |
| IsActive | bool | Index | False on `subscription.end`. |
| StartedAt | timestamp | | |
| EndedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, SubscriberUserId)`. Purpose: a viewer's Twitch sub to the channel (sub-gated commands/leaderboards/goals) — **distinct** from the streamer-billing `Subscriptions` (Domain N). EventSub-driven, never seeded.

### F.3 TwitchFollowers `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| FollowerUserId | guid | FK→Users, Index | Internal surrogate. |
| FollowerTwitchUserId | string(50) | Index | **[PII-hash]**. |
| FollowerDisplayNameSnapshot | string(255) | Null | **[PII-scrub]**. |
| FollowedAt | timestamp | Index | `channel.follow`. |
| StreamId | guid | FK→Streams, Null, Index | Followed-during-stream attribution. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, FollowerUserId)`. Purpose: follow records for goals/alerts/attribution.

### F.4 TwitchChannelEventLog `[APPEND-ONLY]` (read-model)
| Name | Type | Key/Null/Index | Notes |
|---|---|---|---|
| Id | bigint | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| EventType | string(100) | Index | `channel.cheer`,`channel.raid`,`channel.hype_train.begin`,… |
| ActorUserId | guid | FK→Users, Null, Index | Internal surrogate (cheerer/raider). |
| ActorTwitchUserId | string(50) | Null, Index | **[PII-hash]**. |
| ActorDisplayNameSnapshot | string(255) | Null | **[PII-scrub]**. |
| StreamId | guid | FK→Streams, Null, Index | |
| Payload | text | Null | **[VC:JSON]** ids only; free-text scrubbed. |
| OccurredAt | timestamp | Index | |
| CreatedAt | timestamp | | |

Purpose: dashboard activity feed + per-event aggregates without a table per event (read-model side of `EventJournal`).

### F.5 Rewards `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| TwitchRewardId | string(50) | Null, Unique-when-set, Index | Twitch reward UUID. |
| Title | string(255) | | |
| Description | string(500) | Null | |
| Cost | int | Null | Channel points. |
| Prompt | string(500) | Null | |
| IsUserInputRequired | bool | | |
| BackgroundColor | string(7) | Null | |
| IsEnabled | bool | | |
| IsPaused | bool | | |
| MaxPerStream | int | Null | |
| MaxPerUserPerStream | int | Null | |
| GlobalCooldownSeconds | int | Null | |
| ShouldSkipRequestQueue | bool | | |
| PipelineId | guid | FK→Pipelines, Null, Index | Attached pipeline (normalized, not inline JSON). |
| IsManaged | bool | | True = bot's client_id created the reward on Twitch and controls its lifecycle (Helix create/update/delete + fulfill/refund); false = reward exists on Twitch but the bot only observes and reacts. Renamed from `IsPlatform` (inverted: old `IsPlatform=true` "Twitch-native, observe-only" == `IsManaged=false`). Owner: `spec/rewards.md`. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

Purpose: local source-of-truth for a channel-point reward, mirrored to/from Twitch.

### F.6 RewardRedemptions `[APPEND-ONLY]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | bigint | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| RewardId | guid | FK→Rewards, Null, Index | Internal surrogate; null if reward deleted. |
| TwitchRedemptionId | string(50) | Index | External attr (dedupe). |
| RewardTitleSnapshot | string(255) | | Survives reward deletion. |
| RedeemerUserId | guid | FK→Users, Index | Internal surrogate. |
| RedeemerTwitchUserId | string(50) | Index | **[PII-hash]**. |
| RedeemerDisplayNameSnapshot | string(255) | Null | **[PII-scrub]**. |
| UserInput | string(500) | Null | **[PII-scrub]** free text. |
| CostSnapshot | int | Null | Points at redemption. |
| Status | string(20) | Index | `unfulfilled`\|`fulfilled`\|`canceled`. [VC:enum]. |
| StreamId | guid | FK→Streams, Null, Index | |
| EventId | guid | FK→EventJournal.EventId, Null, Index | Enforced FK (`EventJournal.EventId` is Unique → valid target). |
| RedeemedAt | timestamp | Index | |
| CreatedAt | timestamp | | |

**Unique** `(BroadcasterId, TwitchRedemptionId)`. **FK** `EventId`→`EventJournal.EventId`. **Index** `(BroadcasterId, RedeemerUserId)` (erasure scrub of snapshot), `(BroadcasterId, RedeemedAt)` (per-tenant time range). Purpose: each channel-point redemption → pipeline execution, fulfillment, leaderboards, `ViewerProfiles.TotalRedemptions`. (Single canonical table — resolves the Economy `RewardRedemption` / Integrations `TwitchRewardRedemption` overlap.)

### F.7 EventSubSubscriptions `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Provider | string(20) | | `twitch`. [VC:enum]. |
| EventType | string(100) | Index | `stream.online`,… |
| Version | string(20) | | Topic version (upcaster anchor). |
| Condition | text | | **[VC:JSON]** subscription condition. |
| Transport | string(20) | | `websocket`\|`conduit`\|`webhook`. [VC:enum]. |
| TwitchSubscriptionId | string(255) | Null, Index | |
| SessionId | string(255) | Null | WS session (self-host). |
| ConduitId | string(255) | Null | Conduit (SaaS). |
| ShardId | string(255) | Null | |
| Status | string(20) | Index | `pending`\|`enabled`\|`failed`\|`revoked`. [VC:enum]. |
| Enabled | bool | | |
| Cost | int | Null | |
| LastError | string(1000) | Null | |
| ExpiresAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Provider, EventType, Version)`. Purpose: transport-agnostic registry of every EventSub subscription (supersedes `EventSubscription`, adds WS/conduit fields).

### F.8 EventSubConduits `[GLOBAL]` — SaaS shared conduit
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| Provider | string(20) | Index | `twitch`. [VC:enum]. |
| ConduitId | string(255) | Unique, Index | Twitch conduit id (app-global, survives restart). |
| ShardCount | int | | Provisioned shard count. |
| Status | string(20) | Index | `active`\|`degraded`\|`reprovisioning`\|`revoked`. [VC:enum]. |
| LastReconciledAt | timestamp | Null | Last shard-assignment reconcile with Twitch. |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `ConduitId`. Purpose: the app-global SaaS conduit (id + shard count), parent of its shards. `EventSubSubscriptions.ConduitId` denormalizes this id; the conduit object itself (transport `conduit`) lives here so it survives restart and isn't re-derived per subscription.

### F.9 EventSubConduitShards `[GLOBAL]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| ConduitId | guid | FK→EventSubConduits, Index | Owning conduit. |
| ShardId | string(255) | Index | Twitch shard id (0..ShardCount-1). |
| Transport | string(20) | | `webhook`\|`websocket`. [VC:enum]. |
| CallbackUrl | string(2048) | Null | Webhook transport. |
| SessionId | string(255) | Null | WS transport session. |
| Status | string(20) | Index | `enabled`\|`webhook_callback_verification_pending`\|`disabled`. [VC:enum]. |
| AssignedAt | timestamp | Null | |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(ConduitId, ShardId)`. Purpose: per-shard assignment for the shared conduit — lets shard health/transport be tracked and reassigned independently.

### F.10 StreamPresets `[soft-delete]` — saved title/game/tag presets
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(100) | Index | Preset label (e.g. "Just Chatting", "Valorant ranked"). |
| Title | string(255) | Null | Stream title to apply. |
| GameId | string(50) | Null, Index | Twitch category id (attribute). |
| GameName | string(255) | Null | |
| Language | string(8) | Null | |
| Tags | text | Null | **[VC:JSON]** `List<string>`. |
| ContentLabels | text | Null | **[VC:JSON]** `List<string>`. |
| IsBrandedContent | bool | | |
| SortOrder | int | | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Name)`. Purpose: per-game/per-segment title+game+tag presets a streamer can apply in one click. `Channels` holds only the **current** values; this stores reusable presets.

### F.11 ScheduledStreamChanges `[soft-delete]` — scheduled title/game/tag changes
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| StreamPresetId | guid | FK→StreamPresets, Null, Index | Apply a saved preset, or use the inline fields below. |
| Title | string(255) | Null | Inline override. |
| GameId | string(50) | Null | |
| GameName | string(255) | Null | |
| Tags | text | Null | **[VC:JSON]** `List<string>`. |
| ContentLabels | text | Null | **[VC:JSON]** `List<string>`. |
| ScheduledFor | timestamp | Index | When to apply (UTC). |
| Status | string(20) | Index | `pending`\|`applied`\|`failed`\|`canceled` [VC:enum]. |
| AppliedAt | timestamp | Null | |
| LastError | string(1000) | Null | |
| CreatedByUserId | guid | FK→Users, Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Index** `(BroadcasterId, ScheduledFor)`, `(Status, ScheduledFor)` (due-now sweep). Purpose: queued title/game/tag changes to apply at a scheduled time (e.g. mid-stream segment switch). `Channels` holds only current values; this drives the scheduler.

### F.12 ActivePolls `[soft-delete]` — live-poll mirror (broadcaster-liveops)
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| TwitchPollId | string(50) | Index | Twitch poll id (reconciliation key). |
| Title | string(60) | | |
| Choices | text | | **[VC:JSON]** `List<ActivePollChoice>` — `ActivePollChoice(Title, TwitchChoiceId?, Votes)`. |
| DurationSeconds | int | | |
| ChannelPointsVotingEnabled | bool | | |
| ChannelPointsPerVote | int | Null | |
| Status | string(20) | Index | `active`\|`completed`\|`terminated`\|`archived` [VC:enum]. |
| StartedByUserId | guid | FK→Users, Null | |
| StartedAt | timestamp | | |
| EndsAt | timestamp | | |
| EndedAt | timestamp | Null | |
| WinningChoiceTitle | string(60) | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Filtered-unique** `(BroadcasterId)` WHERE `Status='active' AND DeletedAt IS NULL` (one live poll per channel); **Index** `(BroadcasterId, TwitchPollId)`. Purpose: thin pre-EventSub mirror of the active poll so the dashboard shows live state; reconciled from the ingested `PollBeganEvent`/`PollEndedEvent` (twitch-eventsub) by `ILiveOpsReconciler`. Twitch is system of record.

### F.13 ActivePredictions `[soft-delete]` — live-prediction mirror (broadcaster-liveops)
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| TwitchPredictionId | string(50) | Index | Twitch prediction id (reconciliation key). |
| Title | string(45) | | |
| Outcomes | text | | **[VC:JSON]** `List<ActivePredictionOutcome>` — `ActivePredictionOutcome(Title, TwitchOutcomeId?, Color?, ChannelPoints, Users)`. |
| PredictionWindowSeconds | int | | |
| Status | string(20) | Index | `active`\|`locked`\|`resolved`\|`canceled` [VC:enum]. |
| WinningOutcomeId | string(50) | Null | |
| StartedByUserId | guid | FK→Users, Null | |
| StartedAt | timestamp | | |
| LocksAt | timestamp | | |
| LockedAt | timestamp | Null | |
| EndedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Filtered-unique** `(BroadcasterId)` WHERE `Status IN ('active','locked') AND DeletedAt IS NULL`; **Index** `(BroadcasterId, TwitchPredictionId)`. Purpose: thin mirror of the active prediction; reconciled from the ingested `PredictionBeganEvent`/`PredictionLockedEvent`/`PredictionEndedEvent` (twitch-eventsub). Twitch is system of record.

---

## DOMAIN G — Content: Chat & Commands

### G.1 ChatMessages `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | bigint | PK | |
| TwitchMessageId | string(255) | Index | For reply/dedupe/moderation. |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| StreamId | guid | FK→Streams, Null, Index | Session windowing. |
| AuthorUserId | guid | FK→Users, Index | Internal surrogate. |
| AuthorTwitchUserId | string(50) | Index | **[PII-hash]**. |
| AuthorUsernameSnapshot | string(255) | | **[PII-scrub]**. |
| AuthorDisplayNameSnapshot | string(255) | | **[PII-scrub]**. |
| AuthorColorHex | string(7) | Null | |
| Content | text | | **[PII-scrub]** (or [PII-shred] when at-rest encryption on). |
| Fragments | text | | **[VC:JSON]** emote/mention parse (mentions = PII). |
| Badges | text | | **[VC:JSON]**. |
| MessageType | string(50) | | [VC:enum]. |
| IsCommand | bool | | |
| IsCheer | bool | | |
| BitsAmount | int | Null | |
| IsHighlighted | bool | | |
| ReplyToTwitchMessageId | string(255) | Null, Index | |
| CreatedAt | timestamp | Index | Index for per-user history + erasure scrub. |
| UpdatedAt | timestamp | | |
| DeletedAt | timestamp | Null | Mod-deleted or purge. |

**Unique** `(BroadcasterId, TwitchMessageId)`. **Index** `(BroadcasterId, AuthorUserId, CreatedAt)` (per-user history in mod panel + erasure scrub by user). Purpose: per-tenant chat log, FK to surrogate user; anonymizable.

### G.2 Commands `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(100) | | Trigger keyword (no prefix; prefix is applied per `PrefixMode` at match time). |
| NameNormalized | string(100) | Index | Lowercased `Name`; the unique constraint references it (case-insensitive). |
| PrefixMode | string(20) | | `Default`\|`Custom`\|`None`. [VC:enum]. `Default` = channel `Channels.DefaultCommandPrefix`; `Custom` = `CustomPrefix`; `None` = no prefix. Built-ins default to `Default`. |
| CustomPrefix | string(8) | Null | Used only when `PrefixMode=Custom` (e.g. `?`, `+`). Null otherwise. |
| MatchMode | string(20) | | `StartsWith`\|`Exact`\|`Contains`\|`Regex`. [VC:enum]. Default `StartsWith`. (`Regex` matches via `IRegexMatcher` — `RegexOptions.NonBacktracking`, linear-time/ReDoS-safe by construction, no sandbox; commands-pipelines §6.4.) |
| MatchPattern | string(200) | Null | Author regex; required only when `MatchMode=Regex`, null otherwise. Validated + `NonBacktracking`-compiled at save via `IRegexMatcher.ValidateAndCompile` (rejects backreference/lookaround/atomic-group + over-length). |
| Tier | string(20) | Index | `template`\|`pipeline`\|`code`. [VC:enum]. |
| Description | string(500) | Null | |
| Aliases | text | | **[VC:JSON]** `List<string>`. |
| TemplateResponse | string(2000) | Null | T1 single. |
| TemplateResponses | text | Null | **[VC:JSON]** T1 set. |
| ConfigSchemaVersion | int | | App-interpreted-JSON shape version (default 1) for upcasting `TemplateResponses`. |
| PipelineId | guid | FK→Pipelines, Null, Index | T2/T3. |
| MinPermissionLevel | int | Index | Community ladder min. [VC:enum]. |
| CooldownSeconds | int | | |
| UserCooldownSeconds | int | | 0 = off. |
| CooldownPerUser | bool | | |
| IsEnabled | bool | Index | |
| IsPlatform | bool | | |
| UseCount | bigint | | |
| LastUsedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, NameNormalized)`. Purpose: a chat command across all three tiers (template/pipeline/code) — no inline JSON blob.

### G.2a ChannelBuiltinCommands `[soft-delete]` — built-in/seeded command toggles
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| BuiltinKey | string(100) | Index | Stable key of the seeded command (e.g. `followage`, `uptime`, `shoutout`). |
| IsEnabled | bool | Index | Per-channel enable/disable of a built-in (no authored row needed). |
| ConfigSchemaVersion | int | | Default 1; upcast anchor for `OverridesJson`. |
| OverridesJson | text | Null | **[VC:JSON]** optional per-channel overrides (cooldown, min role, response). |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, BuiltinKey)`. Purpose: enable/disable + override state for **seeded/built-in** commands (e.g. `!followage`), distinct from authored `Commands`. Closes the CLAUDE.md "commands show 0 / seeding skipped" known issue: a channel with no authored commands still has built-in toggles, so a missing seed never presents as zero commands.

### G.3 CommandCooldownStates
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | bigint | PK | |
| CommandId | guid | FK→Commands, Index | |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| UserId | guid | FK→Users, Null | Null = global per-command; non-null = per-user. |
| LastInvokedAt | timestamp | Index | |
| ExpiresAt | timestamp | Index | TTL sweep. |

**Unique** `(CommandId, UserId)`. Purpose: high-frequency cooldown writes kept off the config row.

### G.4 NamedCounters `[soft-delete]` — persistent cross-command counters
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Key | string(50) | Index | Counter name, e.g. `deaths`. Addressed by `{{count.<name>}}` + `set_counter`/`adjust_counter` actions. |
| Value | bigint | | Current count (signed; `adjust_counter` increments/decrements). |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Key)`. Purpose: author-incrementable, persistent counters that survive across runs and are shared between commands (e.g. `!deaths +1` → `{{count.deaths}}`), distinct from per-run `set_variable` (non-persistent) and the read-only append-only `CommandUsage` count.

### G.5 Quotes `[soft-delete]` — numbered channel quotes
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Number | int | Index | Per-channel monotonic quote number, allocated via `TenantSequences` (Q.3); never reused. |
| Text | string(500) | | The quote body. |
| QuotedDisplayName | string(100) | Null | Who said it. |
| ContextGame | string(100) | Null | Game/category at the time. |
| QuotedAt | timestamp | Null | When said (defaults to creation). |
| CreatedByUserId | guid | FK→Users, Null | Author who added the quote. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Number)`; **Index** `(BroadcasterId, Number)`. Purpose: a numbered, searchable channel quote (owned by `quotes.md`). Surfaced via the `!quote` built-in and the `post_quote` pipeline action.

### G.6 Giveaways `[soft-delete]` — giveaway campaigns
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Title | string(140) | | Campaign title. |
| EntryMode | string(20) | Index | `keyword`\|`active_viewers`. [VC:enum]. |
| Keyword | string(50) | Null | Entry keyword (keyword mode). |
| EntryCost | bigint | Null | Loyalty-points cost; null/0 = free. |
| MaxEntriesPerUser | int | | Default 1. |
| EligibilityJson | text | Null | **[VC:JSON]** opt-in eligibility filters; empty = everyone. |
| WeightingJson | text | Null | **[VC:JSON]** sub-luck ticket weighting; null = 1 ticket each. |
| WinnerCount | int | | Default 1. |
| ExcludeModerators | bool | | Mods excluded from winning when true (broadcaster always excluded). |
| ClaimWindowMinutes | int | Null | Null = no claim window; unclaimed winner auto-forfeits → re-roll. |
| PrizeMode | string(20) | | `announce`\|`currency`\|`pipeline`\|`code_pool`. [VC:enum]. |
| PrizeCurrencyAmount | bigint | Null | Fixed currency prize (currency mode). |
| PrizeFromPot | bool | | Credit the summed entry-cost pot instead of a fixed amount. |
| PrizePipelineId | guid | FK→Pipelines, Null | Run once per winner (pipeline mode). |
| PrizeCodePoolId | guid | FK→GiveawayCodePools, Null | Source pool (code_pool mode). |
| Status | string(20) | Index | `draft`\|`open`\|`closed`\|`drawn`\|`archived`. [VC:enum]. |
| OpenedAt | timestamp | Null | When opened for entries. |
| ClosesAt | timestamp | Null | Scheduled close. |
| DrawnAt | timestamp | Null | When winners drawn. |
| ConfigSchemaVersion | int | | Per-row upcast anchor for the JSON config columns (default 1). |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `PrizePipelineId`→`Pipelines.Id`, `PrizeCodePoolId`→`GiveawayCodePools.Id`. **Index** `(BroadcasterId, Status)`. Purpose: a giveaway campaign (owned by `giveaways.md`).

### G.7 GiveawayEntries `[soft-delete]` — viewer entries
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| GiveawayId | guid | FK→Giveaways, Index | Owning giveaway. |
| ViewerUserId | guid | FK→Users, Index | Entrant (get-or-create User). |
| ViewerTwitchUserId | string(50) | | **[PII-hash]**. |
| TicketCount | int | | Weighted ticket count (D4). |
| EntryCostLedgerEntryId | bigint | Null | Entry-cost debit (when paid). |
| EnteredAt | timestamp | | When entered. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `GiveawayId`→`Giveaways.Id`, `ViewerUserId`→`Users.Id`. **Unique** `(GiveawayId, ViewerUserId)`. Purpose: one entry per viewer per giveaway (owned by `giveaways.md`).

### G.8 GiveawayWinners `[APPEND-ONLY]` — winner history
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| GiveawayId | guid | FK→Giveaways, Index | Owning giveaway. |
| ViewerUserId | guid | FK→Users, Index | Winner (get-or-create User). |
| ViewerTwitchUserId | string(50) | | **[PII-hash]**. |
| DrawnAt | timestamp | | When drawn. |
| Status | string(20) | Index | `drawn`\|`claimed`\|`forfeited`\|`redrawn`. [VC:enum]. |
| IsRedraw | bool | | True if this row replaced a forfeited/absent winner. |
| AssignedCodeId | guid | FK→GiveawayCodes, Null | Assigned code (code_pool mode). |
| FulfillmentLedgerEntryId | bigint | Null | Currency payout ledger entry (currency mode). |
| WhisperDelivered | bool | Null | Code whisper outcome (code_pool mode); null otherwise. |
| CreatedAt | timestamp | | Append-only — no UpdatedAt/DeletedAt. |

**FK** `BroadcasterId`→`Channels.Id`, `GiveawayId`→`Giveaways.Id`, `ViewerUserId`→`Users.Id`, `AssignedCodeId`→`GiveawayCodes.Id`. **Index** `(GiveawayId)`, `(BroadcasterId, DrawnAt)`. Purpose: append-only winner history per draw (owned by `giveaways.md`).

### G.9 GiveawayCodePools `[soft-delete]` — code-key pools
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(100) | | Pool name. |
| Description | string(300) | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`. Purpose: a named pool of single-use prize codes (owned by `giveaways.md`).

### G.10 GiveawayCodes `[soft-delete]` — encrypted prize codes
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| CodePoolId | guid | FK→GiveawayCodePools, Index | Owning pool. |
| CodeCipher | text | | **[PII-shred]** AEAD ciphertext via `IFieldCipher` (D6 — never plaintext at rest or in reads). |
| Label | string(100) | Null | |
| Status | string(20) | Index | `available`\|`assigned`\|`delivered`\|`revoked`. [VC:enum]. |
| AssignedWinnerId | guid | FK→GiveawayWinners, Null | Winner the code was claimed for. |
| AssignedAt | timestamp | Null | When assigned. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `CodePoolId`→`GiveawayCodePools.Id`, `AssignedWinnerId`→`GiveawayWinners.Id`. **Index** `(CodePoolId, Status)`. Purpose: a single-use, secret prize code stored AEAD-encrypted (owned by `giveaways.md`).

### G.11 EngagementConfigs `[soft-delete]` — per-channel engagement-trigger config
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Unique | Tenant; one config per channel. |
| FirstTimeChatterEnabled | bool | | Default false (opt-in, default-deny). |
| ReturningChatterEnabled | bool | | Default false. |
| WatchStreakEnabled | bool | | Default false. |
| StreakMilestonesJson | text | Null | **[VC:JSON]** (`int[]`, e.g. `[5,10,25,50,100]`; empty = every stream). |
| GreetCooldownSeconds | int | | Default 5 — rate-limits greeting bursts. |
| ConfigSchemaVersion | int | | Per-row upcast anchor for the JSON config column (default 1). |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`. **Unique** `BroadcasterId`. Purpose: per-channel engagement-trigger config (owned by `engagement.md`).

### G.12 ViewerEngagementStates `[soft-delete]` — per-viewer engagement detection state
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| ViewerUserId | guid | FK→Users, Index | Viewer (get-or-create User). |
| ViewerTwitchUserId | string(50) | | **[PII-hash]**. |
| FirstChatAt | timestamp | | First-ever message in this channel. |
| LastChatAt | timestamp | | Most recent message. |
| LastSeenStreamSessionId | guid | Null | Stream they last chatted in. |
| LastGreetedStreamSessionId | guid | Null | Greet-dedup (one greet per viewer per stream). |
| ConsecutiveStreams | int | | Self-owned streak counter. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `ViewerUserId`→`Users.Id`. **Unique** `(BroadcasterId, ViewerUserId)`. Purpose: per-viewer engagement detection state (first-chat/streak) (owned by `engagement.md`).

### G.13 CustomDataSources `[soft-delete]` — channel-defined custom data source
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(50) | | The `<name>` in `custom.<name>` (lowercase slug). |
| DisplayName | string(100) | | |
| SourceKind | string(20) | | **[VC:enum]** `push`\|`poll`\|`socket`. |
| PresetKey | string(50) | Null | The `ICustomDataSourcePreset` key (null = hand-rolled). |
| EndpointUrl | string(500) | Null | Poll/socket URL (null for push). |
| AuthSecretCipher | text | Null, **[PII]** | AEAD via `IFieldCipher` (bearer/OAuth token for socket/poll/push auth). |
| FieldMapJson | text | **[VC:JSON]** | `{ "<field>": "<jsonpath>" }`. |
| PollIntervalSeconds | int | Null | Poll only; clamped to a tier-scaled floor. |
| InboundWebhookEndpointId | guid | FK→InboundWebhookEndpoint, Null | Push only — the H.10 endpoint backing this source. |
| IsEnabled | bool | | Default false (opt-in, default-deny). |
| LastReceivedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `InboundWebhookEndpointId`→`InboundWebhookEndpoint.Id`. **Unique** `(BroadcasterId, Name)`. **Index** `BroadcasterId`. Purpose: a channel-defined custom data source (`custom-events.md`) producing normalized `custom.<name>` events; auth secret AEAD; latest value cached (not stored), history in the event journal.

### G.14 ViewerData `[soft-delete]` — writable per-viewer key/value store
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| ViewerUserId | guid | FK→Users, Index | Viewer (get-or-create User). |
| Key | string(50) | | The data key (lowercase slug). |
| Value | text | | String value; numeric ops parse/format as `long`. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**FK** `BroadcasterId`→`Channels.Id`, `ViewerUserId`→`Users.Id`. **Unique** `(BroadcasterId, ViewerUserId, Key)`. **Index** `BroadcasterId`, `ViewerUserId`. Purpose: writable per-viewer key/value store (`per-viewer-data.md`) — the per-viewer analog of `NamedCounter` (G.4); pipeline-set custom data/flags/counters.

---

## DOMAIN H — Pipelines & User-Authored Code

### H.1 Pipelines `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(200) Index`; `Description string(500) Null`; `TriggerKind string(30) Index` (`command`\|`event`\|`timer`\|`manual`\|`webhook` [VC:enum]); `IsEnabled bool Index`; `MaxStepCount int`; `TriggerCount bigint`; `LastTriggeredAt timestamp Null`; `GraphJsonCache text Null` **[VC:JSON]** (cache; rows below are truth); `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: named, normalized action pipeline reusable by commands, event responses, timers.

### H.2 PipelineSteps
`Id guid PK`; `PipelineId guid FK→Pipelines Index`; `BroadcasterId guid FK→Channels Index`; `ParentStepId guid FK→PipelineSteps Null Index` (branch/block nesting; null = top-level step); `Branch string(10) Null` (`then`\|`else` [VC:enum] — which slot a child occupies under an `if` block); `BlockKind string(20) Null` (`if`\|`switch`\|`switch_case`\|`loop`\|`random_branch`\|`random_case` [VC:enum]; null = leaf action step); `BlockConfigJson text Null` **[VC:JSON]** (block params: `if`/`while` condition; switch value/case-match + comparison; loop mode/list-var/count; case weight); `Order int`; `ActionType string(60) Index` (snake_case registry; unknown = save-fail); `ConfigJson text` **[VC:JSON]** (no tenant/credential/url); `ConfigSchemaVersion int` (default 1; per-row upcast anchor for `ConfigJson`); `CodeScriptId guid FK→CodeScripts Null Index` (only when `run_code`); `IsEnabled bool`; `CreatedAt/UpdatedAt`.
**Unique** `(PipelineId, Order)`. **FK** `ParentStepId`→`PipelineSteps.Id`. **Index** `ParentStepId`. Purpose: one action block; `run_code` references an authored script. `Order` is **order-within-parent** and the step set forms a **tree** — block steps (`BlockKind` non-null) own ordered child steps via `ParentStepId`, walked depth-first (owner `pipeline-control-flow.md`).

### H.3 PipelineStepConditions
`Id guid PK`; `PipelineStepId guid FK→PipelineSteps Index`; `BroadcasterId guid FK→Channels Index`; `ConditionType string(40) Index` (`user_role`\|`random`\|`var_compare`\|`cooldown`; unknown = fail-closed [VC:enum]); `Operator string(20) Null` [VC:enum]; `LeftOperand string(500) Null`; `RightOperand string(500) Null`; `Negate bool`; `Order int`; `CreatedAt/UpdatedAt`.
Purpose: typed, queryable, fail-closed step conditions.

### H.4 PipelineExecutions `[APPEND-ONLY]`
`Id bigint PK`; `PipelineId guid FK→Pipelines Index`; `BroadcasterId guid FK→Channels Index`; `TriggeredByUserId guid FK→Users Null Index`; `TriggerKind string(30)`; `Status string(20) Index` (`success`\|`failed`\|`timeout`\|`denied` [VC:enum]); `HostCallCount int`; `DurationMs int`; `ErrorMessage string(1000) Null`; `StepLogsJson text Null` **[VC:JSON]** bounded, PII-excluded, TTL-purged; `StartedAt timestamp Index`; `CompletedAt timestamp Null`.
Purpose: per-run telemetry (debug, DoS accounting, denial audit).

### H.5 CodeScripts `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(100)`; `Description string(500) Null`; `Language string(20)` (`typescript` [VC:enum]); `CurrentVersionId guid FK→CodeScriptVersions Null Index`; `IsEnabled bool Index`; `AuthorUserId guid FK→Users Null Index`; `LastRuntimeError text Null`; `LastRanAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, Name)`. Purpose: named T3 script; active-version pointer enables hot-swap; instance-level last runtime failure/run time for debugging.

### H.6 CodeScriptVersions `[APPEND-ONLY]`
`Id guid PK`; `CodeScriptId guid FK→CodeScripts Index`; `BroadcasterId guid FK→Channels Index`; `Version int`; `SourceCode text`; `CompiledJs text Null`; `CompiledHash string(64) Index`; `ValidationStatus string(20) Index` (`valid`\|`rejected`\|`pending` [VC:enum]); `ValidationErrorsJson text Null` **[VC:JSON]**; `DeclaredCapabilitiesJson text` **[VC:JSON]**; `PublishedAt timestamp Null`; `AuthorUserId guid FK→Users Null`; `CreatedAt`.
**Unique** `(CodeScriptId, Version)`. Purpose: immutable versioned snapshots with validation + declared capabilities.

### H.7 HttpEgressAllowlist `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Fqdn string(253)`; `ApprovedByUserId guid FK→Users Null`; `IsEnabled bool Index`; `MaxResponseBytes int`; `MaxRequestBytes int` (default 8192 — outbound request-body cap; **reject** when exceeded, not truncate); `AllowRequestBody bool` (default false); `AllowQuery bool` (default false — a guest may attach an arbitrary query string to an egress request only when opted in; second-order SSRF reduction); `AllowedMethods string(100)` (CSV of permitted HTTP methods, default `GET`); `PathPrefix string(255) Null` (optional path-prefix restriction, null = any path); `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, Fqdn)`. Purpose: per-tenant owner-approved egress destinations for `http_request`/`bot.http.fetch`/outbound webhooks (SSRF boundary); request-cap columns clamp outbound exfil + scope second-order SSRF (`code-execution-sandbox.md` §7.1, `custom-code.md` §4, `webhooks.md` §1). **Reused unchanged by outbound webhooks** — `OutboundWebhookEndpoints` (H.8) point at an enabled row here for their egress boundary; no webhook-specific column is added (the request-cap/method/path columns already cover the webhook case).

### H.8 OutboundWebhookEndpoints `[soft-delete]` — user/third-party outbound webhooks
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(200) | | Author label. |
| Fqdn | string(253) | | Target host; **must match an enabled `HttpEgressAllowlist` (H.7) row** for this tenant (broker pattern — secret/url live here, SSRF boundary on H.7). |
| HttpEgressAllowlistId | guid | FK→HttpEgressAllowlist, Null, Index | The approved egress row this endpoint pins to. |
| Path | string(255) | Null | Request path (subject to the H.7 row's `PathPrefix`). |
| SubscribedEventTypesJson | text | **[VC:JSON]** `List<string>` | Event types this endpoint receives; `*` = all. |
| BodyTemplate | text | Null | Author template rendered by `ITemplateEngine`; null = canonical JSON envelope. |
| CustomHeadersJson | text | Null, **[VC:JSON]** `Dictionary<string,string>` | Author headers (templated); reserved `webhook-*`/signature headers rejected. |
| SigningSecretCipher | string(512) | **[PII-shred]** | AEAD-wrapped `whsec_` signing secret (AES-256-GCM; AAD binds tenant+endpoint). |
| SigningSecretNonce | string(255) | | GCM nonce for `SigningSecretCipher`. |
| SecondarySigningSecretCipher | string(512) | Null, **[PII-shred]** | Overlap-valid secret during rotation → Standard-Webhooks multi-sig. |
| SecondarySigningSecretNonce | string(255) | Null | GCM nonce for the secondary secret. |
| EncryptionKeyId | guid | FK→CryptoKey, Index | DEK behind the cipher columns (crypto-shred target). |
| IsEnabled | bool | Index | |
| ConsecutiveFailureCount | int | | Reset on any 2xx; trips auto-disable at threshold (default 20). |
| DisabledAt | timestamp | Null | Set when auto-disabled. |
| DisabledReason | string(255) | Null | |
| LastDeliveryAt | timestamp | Null | |
| LastSuccessAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Name)`. Purpose: per-channel outbound webhook endpoints (Standard-Webhooks signed, SSRF-gated via H.7). Owner `webhooks.md` §1.

### H.9 OutboundWebhookDeliveries `[APPEND-ONLY]` — delivery/retry/dead-letter log
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | bigint | PK | Append order. |
| BroadcasterId | guid | FK→Channels, Index | Tenant (denormalized for RLS). |
| EndpointId | guid | FK→OutboundWebhookEndpoints, Index | Owning endpoint. |
| WebhookMessageId | guid | Index | The `webhook-id` we signed + sent (the receiver's dedupe key). |
| JournalEventId | guid | FK→EventJournal.EventId, Null, Index | The event that triggered this send (FK target is Unique). |
| EventType | string(150) | Index | |
| Attempt | int | | 1-based attempt counter. |
| Status | string(20) | Index | `pending`\|`delivered`\|`failed`\|`dead_letter`. [VC:enum]. |
| ResponseCode | int | Null | HTTP status from the receiver. |
| DurationMs | int | Null | |
| NextRetryAt | timestamp | Null, Index | When `Status=failed`; drives the retry-drain scan. |
| Error | string(1000) | Null | Scrubbed transport/HTTP error. |
| CreatedAt | timestamp | Index | |

**Index** `(EndpointId, CreatedAt)`, `(Status, NextRetryAt)` (retry-drain). Purpose: append-only audit of every outbound attempt (retry + dead-letter); idempotency itself is `IdempotencyKey` (O.4, `Scope="webhook:out"`), not a column here. Owner `webhooks.md` §1.

### H.10 InboundWebhookEndpoints `[soft-delete]` — third-party inbound webhooks
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(200) | | Author label. |
| Token | string(64) | Unique, Index | Opaque unguessable per-endpoint URL token (mirrors `Channels.OverlayToken` model; **not PII**). The `/api/v1/webhooks/in/{token}` path segment. |
| AdapterKind | string(20) | Index | `supporter`\|`github`\|`generic`\|`customdata`. [VC:enum]. (`supporter` = the generic supporter webhook adapter; dispatches by `SupporterConnection.SourceKey` — Ko-fi/Patreon/Fourthwall/Shopify — per-provider HMAC verify then `ISupporterIngestService`.) |
| VerificationSecretCipher | string(512) | **[PII-shred]** | AEAD-wrapped per-provider secret/token (AAD binds tenant+endpoint). |
| VerificationSecretNonce | string(255) | | GCM nonce. |
| EncryptionKeyId | guid | FK→CryptoKey, Index | DEK behind the cipher (crypto-shred target). |
| GenericConfigJson | text | Null, **[VC:JSON]** | `GenericInboundConfig` (signature header/signing-string/shared-secret-in-body) — only when `AdapterKind=generic`. |
| TargetPipelineId | guid | FK→Pipelines, Null, Index | Pipeline to run on a verified hit; null = fan out via `IEventResponseService`. |
| TargetEventType | string(100) | Null | Overrides the derived `webhook.<provider>.<kind>` event type. |
| IsEnabled | bool | Index | |
| LastReceivedAt | timestamp | Null | |
| ReceiveCount | bigint | | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `Token`, `(BroadcasterId, Name)`. Purpose: per-channel inbound webhook endpoints (Ko-fi/GitHub/generic), verified per-adapter, deduped via `IdempotencyKey` (O.4), journaled as `EventJournal.Source="webhook"`. Owner `webhooks.md` §1.

### H.11 InstalledBundle `[soft-delete]` — installed import/marketplace bundles
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Name | string(150) | | Bundle display name. |
| Source | string(20) | | `local`\|`marketplace`. [VC:enum]. |
| MarketplaceItemId | string(64) | Null | Null for local ZIPs; the marketplace item id otherwise. |
| Version | string(40) | | Installed bundle version. |
| Author | string(100) | Null | |
| License | string(40) | Null | |
| ManifestJson | text | **[VC:JSON]** | The `BundleManifest`. |
| InstalledEntityIdsJson | text | **[VC:JSON]** | `{ type → Guid[] }` — created entity ids (for update/uninstall). |
| InstalledByUserId | guid | FK→Users, Index | Actor who installed. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**PK** `Id`. **FK** `BroadcasterId`→`Channels.Id`, `InstalledByUserId`→`Users.Id`. **Unique** `(BroadcasterId, Source, MarketplaceItemId)`. **Index** `BroadcasterId`. Purpose: tracks an installed import/marketplace bundle (`marketplace.md`) for update/uninstall; the marketplace catalog itself lives in the separate NoMercy-hosted marketplace service.

---

## DOMAIN I — Content: Timers & Event Responses

### I.1 Timers `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(100) Index`; `Messages text` **[VC:JSON]**; `ConfigSchemaVersion int` (default 1; upcast anchor for `Messages`); `PipelineId guid FK→Pipelines Null Index`; `IntervalMinutes int`; `MinChatActivity int`; `IsEnabled bool Index`; `LastFiredAt timestamp Null`; `NextMessageIndex int`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: scheduled rotating chat messages (or pipeline triggers) with activity gating.

### I.2 EventResponses `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `EventType string(100) Index`; `ResponseType string(50)` (`chat_message`\|`overlay`\|`pipeline`\|`none` [VC:enum]); `Message string(2000) Null`; `PipelineId guid FK→Pipelines Null Index`; `MetadataJson text` **[VC:JSON]** `Dictionary<string,string>`; `ConfigSchemaVersion int` (default 1; upcast anchor for `MetadataJson`); `IsEnabled bool Index`; `CreatedAt/UpdatedAt/DeletedAt`.
**Index** `(BroadcasterId, EventType)`. Purpose: per-event bot reaction; pipeline responses reference normalized `Pipelines`.

---

## DOMAIN J — Moderation

### J.1 ModerationQueueItems `[soft-delete]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `Source string(20) Index` (`automod`\|`viewer_report`\|`bot_flag` [VC:enum]); `Status string(20) Index` (`pending`\|`approved`\|`denied`\|`actioned`\|`expired` [VC:enum]); `TargetUserId guid FK→Users Index`; `TargetTwitchUserId string(50) Index` **[PII-hash]**; `TargetUsernameSnapshot string(255) Null` **[PII-scrub]**; `ChatMessageId bigint FK→ChatMessages Null Index`; `MessageContentSnapshot text Null` **[PII-scrub]**; `ReportedByUserId guid FK→Users Null Index`; `Reason string(500) Null`; `AutoModCategory string(50) Null`; `ResolvedByUserId guid FK→Users Null Index`; `ResolvedAt timestamp Null Index`; `ResolutionAction string(20) Null` [VC:enum]; `ExpiresAt timestamp Null Index`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: unified mod queue (AutoMod holds + reports + bot flags) with inline resolution.

### J.2 ModerationActions `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `ActionType string(20) Index` (`ban`\|`unban`\|`timeout`\|`untimeout`\|`delete_message`\|`warn`\|`nuke` [VC:enum]); `TargetUserId guid FK→Users Index`; `TargetTwitchUserId string(50) Index` **[PII-hash]**; `TargetUsernameSnapshot string(255) Null` **[PII-scrub]**; `ActorUserId guid FK→Users Index`; `ActorKind string(20)` (`human`\|`bot`\|`automod` [VC:enum]); `Reason string(500) Null` **[PII-scrub]**; `DurationSeconds int Null`; `ChatMessageId bigint FK→ChatMessages Null Index`; `QueueItemId bigint FK→ModerationQueueItems Null Index`; `IsReverted bool Index`; `RevertedByActionId bigint FK→ModerationActions Null`; `Origin string(20)` (`local`\|`shared_chat`\|`network_nuke`\|`federation` [VC:enum]); `OriginChannelId guid FK→Channels Null`; `NetworkNukeBatchId guid FK→NetworkNukeBatches Null Index` (fan-out grouping for one-unit un-nuke); `TwitchActionId string(100) Null`; `CreatedAt Index`.
**Index** `(BroadcasterId, TargetUserId)` (erasure scrub of snapshot), `(BroadcasterId, CreatedAt)` (per-tenant audit timeline). Purpose: authoritative append-only mod-action audit (local + shared-chat + network-nuke) with revert linkage.

### J.2a NetworkNukeBatches `[soft-delete]` — nuke fan-out grouping
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). Referenced by `ModerationActions.NetworkNukeBatchId`. |
| OriginBroadcasterId | guid | FK→Channels, Index | Channel that initiated the nuke. |
| InitiatedByUserId | guid | FK→Users, Null, Index | Actor. |
| MatchTerm | string(500) | Null | **[PII-scrub]** term/pattern the nuke targeted. |
| TargetUserId | guid | FK→Users, Null, Index | Internal surrogate of the nuked viewer (when single-subject). |
| TargetTwitchUserId | string(50) | Null, Index | **[PII-hash]**. |
| ChannelCount | int | | Number of channels the nuke fanned out to. |
| Status | string(20) | Index | `active`\|`reverted`\|`partial` [VC:enum]. |
| RevertedByUserId | guid | FK→Users, Null | Actor of the un-nuke. |
| RevertedAt | timestamp | Null | |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Index** `(OriginBroadcasterId, CreatedAt)`. Purpose: the set of channels one network-nuke hit, so an "un-nuke" reverses every fan-out `ModerationActions` row as one unit. A per-channel action row alone can't enumerate the fan-out; the batch is the grouping key.

### J.3 UserNotes `[soft-delete]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `SubjectUserId guid FK→Users Index`; `SubjectTwitchUserId string(50) Index` **[PII-hash]**; `AuthorUserId guid FK→Users Null Index`; `Content string(2000)` **[PII-scrub]**; `Pinned bool`; `CreatedAt/UpdatedAt/DeletedAt`.
**Index** `(BroadcasterId, SubjectUserId)`. Purpose: shared mod notes for the per-user context panel.

### J.4 UserModerationHistory (projection)
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `SubjectUserId guid FK→Users Index`; `SubjectTwitchUserId string(50) Index` **[PII-hash]**; `TimeoutCount int`; `BanCount int`; `WarningCount int`; `MessagesDeletedCount int`; `LastActionAt timestamp Null Index`; `LastActionType string(20) Null` [VC:enum]; `FirstSeenAt timestamp Null`; `UpdatedAt`.
**Index** `(BroadcasterId, SubjectUserId)`. Purpose: per-user action rollup for the mod panel; rebuildable from `ModerationActions`.

### J.5 UserTrustScores
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `SubjectUserId guid FK→Users Index`; `SubjectTwitchUserId string(50) Index` **[PII-hash]**; `TrustScore decimal(8,4) Index`; `HeatScore decimal(8,4) Index`; `LastHeatEventAt timestamp Null`; `ComputedAt timestamp Index`; `UpdatedAt`.
**Unique** `(BroadcasterId, SubjectUserId)`. Purpose: per-channel trust/heat driving auto-mod thresholds; rebuildable.

### J.6 ChatFilters `[soft-delete]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `FilterType string(20) Index` (`regex`\|`blocklist`\|`link_policy` [VC:enum]); `Name string(100)`; `Pattern string(2000) Null`; `Terms text Null` **[VC:JSON]**; `LinkPolicyJson text Null` **[VC:JSON]**; `Action string(20)` (`delete`\|`timeout`\|`hold`\|`flag`\|`escalate` [VC:enum]; `escalate` defers the action to the J.10 escalation ladder); `TimeoutSeconds int Null`; `ExemptMinRoleLevel int` [VC:enum]; `IsEnabled bool Index`; `IsCaseSensitive bool`; `MatchCount bigint`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: per-channel opt-in filters (regex/blocklist/link) with action + role exemptions.

### J.7 AutoModConfigs
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique** Index`; `IsEnabled bool`; `OverallLevel int`; `CategoryLevelsJson text` **[VC:JSON]**; `HeldMessageTimeoutSeconds int`; `BlockHyperlinks bool`; `RequireVerifiedAccount bool`; `RequireVerifiedEmail bool`; `AutoTimeoutOnHeat bool`; `HeatTimeoutThreshold decimal(8,4) Null`; `BlockedTermsSyncedAt timestamp Null`; `ShieldModeActive bool`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId)`. Purpose: per-channel AutoMod config (Twitch levels + heat-driven auto-actions + hold TTL). `ShieldModeActive` denormalizes Twitch's live Shield Mode toggle (Twitch is system of record; relayed via `IChatControlService`, moderation §3.9).

### J.8 ViewerReports `[soft-delete]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `QueueItemId bigint FK→ModerationQueueItems Null Index`; `ReportedUserId guid FK→Users Index`; `ReportedTwitchUserId string(50) Index` **[PII-hash]**; `ReporterUserId guid FK→Users Null Index`; `Reason string(500)` **[PII-scrub]**; `Status string(20) Index` (`open`\|`triaged`\|`dismissed`\|`escalated` [VC:enum]); `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: viewer reports feeding the queue + legitimate-individual evidence packet (not mass-report). Evidence messages are the `ViewerReportEvidence` join table (J.8a), **not** an inline `List<long>` blob — so each evidence link FK-cascades on chat-message purge/erasure and is queryable.

### J.8a ViewerReportEvidence — report→message join (was `EvidenceMessageIds` blob)
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant (denormalized for RLS). |
| ViewerReportId | bigint | FK→ViewerReports, Index | Owning report. |
| ChatMessageId | bigint | FK→ChatMessages, Index | Cited evidence message (real FK → cascades on purge/erasure). |
| CreatedAt | timestamp | | |

**Unique** `(ViewerReportId, ChatMessageId)`. Purpose: replaces `ViewerReports.EvidenceMessageIds` (`List<long>` in JSON) with a first-class join table — FK-enforced, indexable, joinable, and erasure-cascading. Was a set of FKs hidden in a blob.

### J.9 SharedBanSettings
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique**`; `AcceptSharedChatBans bool`; `ShareOutgoingBans bool`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId)`. Purpose: per-channel opt-in shared-chat ban propagation toggles (default-deny, super-mod gated). The trust-list is the `SharedBanTrustedChannels` join table (J.9a), **not** an inline `List<guid>` blob. Complements `ChannelFederationOptIns` (D.3) for the cross-instance leg.

### J.9a SharedBanTrustedChannels — trust-list join (was `TrustedChannelsJson` blob)
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Trusting tenant. |
| TrustedChannelId | guid | FK→Channels, Index | The trusted channel (real FK → cascades on channel erasure). |
| AddedByUserId | guid | FK→Users, Null | Audit. |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(BroadcasterId, TrustedChannelId)`. Purpose: replaces `SharedBanSettings.TrustedChannelsJson` (`List<guid>` of FKs in a blob) with a first-class join table — FK-enforced, indexable, joinable, and cascade-on-erasure. A trusted-channel relationship is a query-driven entity, not a blob.

### J.10 ModerationEscalationPolicies `[soft-delete]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, **Unique**, Index | Tenant; one policy per channel. |
| IsEnabled | bool | | Off by default; opt-in escalation ladder. |
| LadderJson | text | | **[VC:JSON]** `List<EscalationLadderStep>` — offense-count → action (`warn`\|`timeout`\|`ban`) + optional timeout seconds. Null/empty seeds the safety-baseline default (1→warn, 2→60s, 3→600s, 4→3600s, 5→86400s, 6+→ban). |
| OffenseWindowHours | int | | Decaying-window length; offenses older than this reset the tally. Default 168 (7 days). |
| CountAutoModViolations | bool | | Whether native Twitch AutoMod violations also tick the ladder's offense counter. Default false (ladder counts only `Action=Escalate` filter hits). |
| ConfigSchemaVersion | int | | Default 1; upcast anchor for `LadderJson`. |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId)`. Purpose: per-channel auto-mod escalation-ladder config — the **explicit discrete** escalation path (offense-count → action), complementary to the **continuous** heat-driven path (`UserTrustScores.HeatScore` J.5 + `AutoModConfigs.AutoTimeoutOnHeat`/`HeatTimeoutThreshold` J.7). A `ChatFilter` (J.6) with `Action=escalate` defers to this ladder (owner `moderation.md`).

### J.11 ModerationEscalationStates
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| SubjectUserId | guid | FK→Users, Index | The escalating viewer. |
| SubjectTwitchUserId | string(50) | Index | **[PII-hash]**. |
| OffenseCount | int | | Running count within the current window; clamps to the highest ladder step. |
| WindowStartedAt | timestamp | | Start of the current decaying window; reset when `OffenseWindowHours` elapses. |
| LastOffenseAt | timestamp | | Most recent offense stamp. |
| CreatedAt/UpdatedAt | timestamp | | Mutable tally (no soft-delete; cleared by the forgiveness reset). |

**Unique** `(BroadcasterId, SubjectUserId)`. Purpose: per-subject offense tally driving the J.10 ladder; a mutable counter (not append-only) — the forgiveness reset clears it (owner `moderation.md`).

### J.12 ChannelModerationStanding
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Provider string(20)` (`twitch`\|`youtube`\|`kick` [VC:enum]); `UserId string(64)` — the platform user id, stored **RAW** (deliberate: this is an operational enforcement row equality-matched against live inbound chat ids on the hot path, same posture as `ChatPollVote.VoterUserId` and the RedemptionTimer ids — **not** [PII-hash] like the Domain-J history tables; the operator-facing panel also needs the plain id); `Standing string(20)` (`muted`\|`shadowbanned`\|`blacklisted` [VC:enum]; an absent row means normal — no stored `none`); `Reason string(500) Null`; `CreatedByUserId guid FK→Users Null`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, Provider, UserId)`. **Index** `(BroadcasterId, Standing)`. Purpose: the negative bot-side moderation axis (graduated ignore tiers) — owner `moderation.md` §1 J.12 + §9 decision 3.

---

## DOMAIN K — Economy

### K.1 CurrencyConfig `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique**`; `CurrencyName string(50)`; `CurrencyNamePlural string(50) Null`; `IconUrl string(2048) Null`; `IsEnabled bool`; `StartingBalance bigint`; `MaxBalance bigint Null`; `DecimalPlaces int`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId)`. Purpose: per-channel currency definition.

### K.1a EarningRules `[soft-delete]` — per-source earn rate + cap
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Tenant. |
| Source | string(30) | Index | `watch_time`\|`chat`\|`follow`\|`sub`\|`bits`\|`raid`\|`redemption`. [VC:enum]. |
| IsEnabled | bool | Index | Per-source on/off. |
| Rate | bigint | | Amount earned per unit (per-minute / per-message / per-event / per-100-bits, by `Source`). |
| UnitWindowSeconds | int | Null | Accrual window for rate sources (e.g. watch-time per minute). |
| PerWindowCap | bigint | Null | Max earned per window (anti-farm). |
| PerStreamCap | bigint | Null | Max earned per stream from this source. |
| MinRoleLevel | int | Null | Optional community-level gate. [VC:enum]. |
| ConfigSchemaVersion | int | | Default 1; upcast anchor for `BonusConfigJson`. |
| BonusConfigJson | text | Null | **[VC:JSON]** source-specific bonus rules (sub-tier multipliers, raid-size tiers). |
| CreatedAt/UpdatedAt/DeletedAt | timestamp | DeletedAt Null | |

**Unique** `(BroadcasterId, Source)`. Purpose: per-source (watch-time / chat / follow / sub / bits / raid / redemption) earn rate + caps. `CurrencyConfig` only holds name/start-balance — earning was previously undefined data; this table makes it first-class and queryable.

### K.2 CurrencyAccounts `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `ViewerUserId guid FK→Users Index`; `ViewerTwitchUserId string(50) Index` **[PII-hash]**; `Balance bigint` (projection of ledger); `LifetimeEarned bigint`; `LifetimeSpent bigint`; `IsFrozen bool`; `LastActivityAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, ViewerUserId)`. Purpose: viewer's per-channel wallet; balance folds over the ledger.

### K.3 CurrencyLedgerEntries `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `TenantPosition bigint Index` (per-tenant monotonic; app-assigned under the per-tenant lock via `TenantSequences`, NOT DB auto-increment — see §1.4); `AccountId guid FK→CurrencyAccounts Index`; `ViewerUserId guid FK→Users Index` (denormalized); `ViewerTwitchUserId string(50)` **[PII-hash]**; `Amount bigint` (signed); `BalanceAfter bigint`; `EntryType string(30) Index` (earn_*/spend_*/jar_*/admin_adjust/transfer [VC:enum]); `SourceType string(30) Null` [VC:enum]; `SourceId guid Null`; `RelatedEntryId bigint FK→CurrencyLedgerEntries Null`; `EventId guid FK→EventJournal.EventId Null Index` (enforced FK; `EventJournal.EventId` is Unique → valid target); `Reason string(255) Null` **[PII-scrub]**; `ActorUserId guid FK→Users Null` **[PII via id]**; `CreatedAt Index`.
**Unique** `(BroadcasterId, TenantPosition)` (per-tenant ordering guarantee). **FK** `EventId`→`EventJournal.EventId`. **Index** `(BroadcasterId, AccountId, Id)` (balance fold by account without scan), `(BroadcasterId, ViewerUserId)` (erasure scrub). Purpose: immutable event-sourced currency journal — every balance is a fold; corrections are reversing entries.

### K.4 SavingsJars `[soft-delete, CROSS-TENANT]`
`Id guid PK`; `OwnerBroadcasterId guid FK→Channels Index`; `Name string(100)`; `Description string(500) Null`; `GoalAmount bigint Null`; `Balance bigint`; `IconUrl string(2048) Null`; `IsOpen bool`; `MaxWithdrawalPerChannel bigint Null`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: pooled cross-channel account; RLS is membership-based (see K.5), not single-tenant.

### K.5 SavingsJarMemberships `[soft-delete]`
`Id guid PK`; `JarId guid FK→SavingsJars Index`; `MemberBroadcasterId guid FK→Channels Index`; `Role string(20)` (`owner`\|`partner`\|`viewer` [VC:enum]); `Status string(20)` (`pending`\|`accepted`\|`revoked` [VC:enum]); `ContributionCapPerStream bigint Null`; `WithdrawalCap bigint Null`; `InvitedByBroadcasterId guid Null`; `AcceptedAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(JarId, MemberBroadcasterId)`. Purpose: jar membership + federated trust status + per-channel caps. The membership predicate is the cross-tenant RLS guard.

### K.6 JarContributions `[APPEND-ONLY]`
`Id bigint PK`; `JarId guid FK→SavingsJars Index`; `SourceBroadcasterId guid FK→Channels Index`; `ContributorAccountId guid FK→CurrencyAccounts Null`; `ContributorUserId guid FK→Users Null` **[PII via id]**; `Amount bigint` (signed); `MovementType string(20) Index` (`contribute`\|`withdraw` [VC:enum]); `LedgerEntryId bigint FK→CurrencyLedgerEntries Null`; `ActorUserId guid FK→Users Null`; `CreatedAt Index`.
Purpose: immutable audited jar movement log with federation-enforced source channel + actor.

### K.7 GameConfigs `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GameType string(30) Index` (`slots`/`duel`/`heist`/`wager`/`coinflip`/`roulette`/`trivia`); `Category string(20)` (`minigame`\|`gambling`); `IsEnabled bool`; `Requires18Plus bool` (**default `false`**); `MinBet bigint Null`; `MaxBet bigint Null`; `HouseEdgePercent decimal(5,2) Null`; `WinChancePercent decimal(5,2) Null`; `PayoutMultiplier decimal(8,2) Null`; `CooldownSeconds int`; `MaxPlaysPerStream int Null`; `ConfigJson text Null` **[VC:JSON]**; `Permission string(20)` [VC:enum]; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, GameType)`. Purpose: per-channel fun-money game config with limits, odds, anti-abuse caps. The bet currency is non-purchasable, non-cashable loyalty currency, so this is **not** regulated gambling; `Requires18Plus` is an **optional, off-by-default (`false`)** streamer toggle (community vibe / advertiser-friendliness), **not** a compliance/KYC requirement. When `false`, plays run with no age check (see `economy.md` §3.5).

### K.8 ViewerAgeConsents `[soft-delete]`
> **Consolidated with GDPR `ConsentRecord` (Domain O).** This is the economy-facing view; the authoritative consent row lives in `ConsentRecords` with `ConsentType=age_18_gambling`. Kept as a thin 1:1 cache for fast gambling-gate checks.

`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `ViewerUserId guid FK→Users Index`; `ViewerTwitchUserId string(50) Index` **[PII-hash]**; `ConsentRecordId guid FK→ConsentRecords Null Index` (Null for inferences — they have no consent-ledger row); `Granted bool`; `ConfirmedAt timestamp`; `RevokedAt timestamp Null`; `ConfirmationMethod string(30)` (`chat_command`\|`dashboard`\|`overlay`\|`inferred_account_age`\|`inferred_twitch_personnel` [VC:enum]); `LawfulBasis string(30)` (`consent`\|`legitimate_interest` [VC:enum]; `legitimate_interest` for the two `inferred_*` methods, `consent` otherwise); `InferredAccountCreatedAt timestamp Null` (snapshot basis for `inferred_account_age` — the immutable `Users.CreatedAt` the gate compared); `InferredFromStatus string(20) Null` (snapshot basis for `inferred_twitch_personnel` — the Twitch `type` observed: `staff`\|`admin`\|`global_mod`); `StatusVerifiedAt timestamp Null` (last live re-check of the revocable personnel status; **unused** by the monotonic account-age method); `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, ViewerUserId)`. Purpose: lightweight per-channel cache for the **optional, off-by-default** fun-money 18+ toggle (engages only when a streamer sets `GameConfigs.Requires18Plus=true`; see `economy.md` §3.5/§3.6) — a "remember this viewer is 18+ in this channel" record, NOT a special-category consent store. Age/18+ status is **regular personal data**, not Art. 9 special-category (that treatment is for pronouns, separate). Carries BOTH self-confirmation (`ConfirmationMethod ∈ {chat_command,dashboard,overlay}`, `LawfulBasis=consent`, IP/version proof on the linked `ConsentRecords`) AND provable-adult **inferences** (`ConfirmationMethod ∈ {inferred_account_age,inferred_twitch_personnel}`, `LawfulBasis=legitimate_interest`, `ConsentRecordId` null). The `LawfulBasis`/snapshot columns are kept because they stay useful and honest (each inference auditable + visibly distinct), but no extra-care/special-category handling applies. An inference is **never** materialized as a `ConsentRecords(age_18_gambling,granted,consent)` row — the consent ledger keeps meaning strictly "the human affirmatively self-confirmed". Account-age inference is monotonic (immutable `created_at`, no TTL); personnel inference is revocable (re-checked via `StatusVerifiedAt`). Affiliate/Partner/broadcaster are excluded as adulthood signals (Twitch permits 13–17 minors to hold them). See `economy.md` §3.6.

### K.9 GamePlays `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `GameConfigId guid FK→GameConfigs Index`; `GameSessionId guid FK→GameSessions Null Index` (null for instant `PlayAsync` games; set for every live-game award row — see K.9a); `PlayerAccountId guid FK→CurrencyAccounts Index`; `PlayerUserId guid FK→Users Index` **[PII via id]**; `BetAmount bigint`; `Outcome string(20) Index` (`win`\|`lose`\|`push`\|`jackpot` [VC:enum]); `PayoutAmount bigint`; `NetResult bigint`; `ResultJson text Null` **[VC:JSON]**; `BetLedgerEntryId bigint FK→CurrencyLedgerEntries Null`; `PayoutLedgerEntryId bigint FK→CurrencyLedgerEntries Null`; `CreatedAt Index`.
**Index** `(BroadcasterId, PlayerUserId)` (erasure scrub), `(BroadcasterId, CreatedAt)` (per-tenant time range), `(GameSessionId)` (session-history reads on the new FK). Purpose: per-play record for leaderboards, anti-abuse, ledger correlation.

### K.9a GameSessions `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GameConfigId guid FK→GameConfigs Index`; `GameType string(30) Index`; `Status string(20) Index` (`lobby`\|`running`\|`resolving`\|`settled`\|`cancelled` [VC:enum]); `StartedByUserId guid Null`; `StartedAt timestamp`; `JoinClosesAt timestamp Null`; `ResolvedAt timestamp Null`; `ParticipantCount int`; `StateJson text Null` **[VC:JSON]** (engine/game state snapshot — overlay frame + crash recovery); `OutcomeJson text Null` **[VC:JSON]** (resolved summary); `CancelReason string(60) Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Index** `(BroadcasterId, Status, CreatedAt)`. **Note:** at most one non-terminal (`lobby`/`running`/`resolving`) row per `BroadcasterId` — **service-enforced, NOT a DB unique constraint** (terminal rows accumulate). Purpose: a stateful interactive overlay-game round (owned by `live-games.md`). Instant economy games (GamePlay via `IGameService.PlayAsync`) do not create sessions.

### K.10 CatalogItems `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(100)`; `NameNormalized string(100) Index` (lowercased `Name`; unique constraint references it); `Description string(500) Null`; `SinkType string(30) Index` (`custom_reward`/`sr_priority`/`sr_skip`/`tts`/`alert`/`fun_trigger`/`game_entry`/`role_perk`); `Cost bigint`; `IconUrl string(2048) Null`; `IsEnabled bool`; `Permission string(20)` [VC:enum]; `PipelineId guid FK→Pipelines Null Index` (normalized, not inline JSON); `CooldownSeconds int`; `CooldownPerUser bool`; `StockLimit int Null`; `StockRemaining int Null`; `MaxPerViewerPerStream int Null`; `SortOrder int`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, NameNormalized)`. Purpose: broadcaster spend catalog (every currency sink as data).

### K.11 CatalogPurchases `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `CatalogItemId guid FK→CatalogItems Index`; `BuyerAccountId guid FK→CurrencyAccounts Index`; `BuyerUserId guid FK→Users Index` **[PII via id]**; `CostPaid bigint`; `ItemNameSnapshot string(100)`; `Status string(20) Index` (`completed`\|`pending`\|`refunded`\|`failed` [VC:enum]); `LedgerEntryId bigint FK→CurrencyLedgerEntries Null`; `InputArgs string(500) Null` **[PII-scrub]**; `CreatedAt Index`.
**Index** `(BroadcasterId, BuyerUserId)` (erasure scrub), `(BroadcasterId, CreatedAt)` (per-tenant time range). Purpose: immutable redemption record with price/name snapshot.

---

## DOMAIN L — Engagement: Leaderboards & Song Requests

### L.1 LeaderboardConfigs `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Null Index`; `JarId guid FK→SavingsJars Null Index`; `Metric string(30) Index` (balance/lifetime_earned/watchtime/messages/gamble_net/streak/bits/sr_count); `Scope string(20)` (`channel`\|`jar`); `Period string(20)` (all_time/monthly/weekly/daily/current_stream); `IsPublic bool`; `TopN int`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: defines a ranking view (metric/period/scope/visibility).

### L.2 LeaderboardOptOuts
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `ViewerUserId guid FK→Users Index`; `ViewerTwitchUserId string(50) Index` **[PII-hash]**; `OptedOutAt timestamp`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, ViewerUserId)`. Purpose: GDPR leaderboard opt-out.

### L.3 LeaderboardSnapshots `[APPEND-ONLY]`
`Id bigint PK`; `LeaderboardConfigId guid FK→LeaderboardConfigs Index`; `BroadcasterId guid FK→Channels Null Index`; `PeriodKey string(20) Index`; `Rank int`; `SubjectAccountId guid FK→CurrencyAccounts Null`; `SubjectUserId guid FK→Users Null`; `SubjectTwitchUserId string(50)` **[PII-hash]**; `DisplayNameSnapshot string(255)` **[PII-scrub]**; `Value bigint`; `CapturedAt timestamp Index`.
**Index** `(BroadcasterId, SubjectUserId)` (erasure scrub of snapshot). Purpose: frozen historical standings per closed period; anonymizable while preserving ranks.

### L.4 SongRequestQueues
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique**`; `IsOpen bool`; `IsPaused bool`; `MaxQueueLength int`; `AllowExplicit bool`; `MinYouTubeTrustScore decimal(8,4) Null`; `TrustScoringConfig text Null` **[VC:JSON]** (advanced per-channel buff/debuff tuning for the §3.9 Bamo trust algorithm — the metric base [weights/decays] is FIXED and NOT stored here; this blob holds only the modifier toggles + magnitudes, each defaulting to Bamo's current constant so the default reproduces today's behavior exactly: `ReputationBoostEnabled`=true/`ReputationBoostMinRequests`=10, `FollowPenaltyEnabled`=true/`FollowPenaltyMultiplier`=0.75/`FollowPenaltyMinDays`=1, `YouTubeQualityPenaltyEnabled`=true/`YouTubeQualityMultiplier`=0.75/`MinChannelVideoCount`=5/`MinChannelTotalViews`=5000/`MinChannelSubscribers`=25/`MinChannelAgeMonths`=1, `SkipPenaltyEnabled`=true/`SkipPenalty`=5, `TimeoutPenaltyEnabled`=true/`TimeoutPenalty`=10, `BanPenaltyEnabled`=true/`BanPenalty`=30; validation: multipliers ∈ (0,1], penalties/thresholds ≥ 0; null = all defaults; rides `music:config:write`, mirrors the `PendingLimits` JSON precedent); `SubscriberOnly bool`; `MinStandingToRequest string(20)` (`everyone`\|`subscriber`\|`vip`\|`moderator` [VC:enum]; default `everyone`); `EnabledProviders text Null` **[VC:JSON]** `List<string>` (`["spotify","youtube"]` — which providers accept requests; one or both); `ProviderPriority text Null` **[VC:JSON]** `["spotify","youtube"]` (preferred provider for ambiguous/bare-search requests + the cross-resolve target); `CrossResolveForeignLinks bool` (default true); `PendingLimits text Null` **[VC:JSON]** `Dictionary<string,int?>` (per-standing concurrent-pending cap; keys `everyone`/`subscriber_t1`/`subscriber_t2`/`subscriber_t3`/`vip`/`moderator`/`broadcaster`; `null` value = unlimited; defaults 2/4/4/4/10/null/null); `PaidPendingLimit int Null` (separate channel-point-lane cap; null = off); `PaidExtraSlotEnabled bool` (default false); `QueueJumpEnabled bool` (default false); `PerStreamLimit int Null` (lifetime-in-stream per-user cap; null = off); `MaxDurationFreeSeconds int` (default 360); `MaxDurationPaidSeconds int` (default 600); `StripYouTubeAds bool` (default true); `AutoBumpFirstSong bool` (default false — each requester's first song of the stream is placed in the auto-bump band, above the regular fair queue, below every explicit bump); `RaffleEnabled bool` (default false); `RaffleEntryCost int` (default 0 — channel-point cost per ticket; debited via `CatalogPurchases`); `RaffleTicketsPerUser int` (default 1 — fairness cap, not pay-to-win); `RaffleWinnerCount int` (default 1); `RaffleIntervalMinutes int Null` (null = manual-only `!raffle`; value = auto-run cadence); `SpotifyLockedDeviceId string(255) Null`; `SpotifyLockedDeviceName string(255) Null`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId)`. Purpose: per-channel SR config/state — single interleaved fair queue both providers feed. `MaxPerUser` **removed** — superseded by the per-standing `PendingLimits` (concurrent unplayed items the requester owns) + `PaidPendingLimit`/`PerStreamLimit`. `SpotifyLockedDevice*` remember the streamer's preferred Spotify device across sessions (a fully-closed Spotify reports no devices, so the name backs the connection nudge in `music-sr.md` §3.x).

### L.5 SongRequestItems `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `QueueId guid FK→SongRequestQueues Index`; `Provider string(20)` (`spotify`\|`youtube` [VC:enum]); `ProviderTrackId string(255) Index`; `Title string(500) Null`; `Artist string(500) Null`; `DurationSeconds int Null`; `ThumbnailUrl string(2048) Null`; `RequestedByUserId guid FK→Users Index`; `RequestedByTwitchUserId string(50) Index` **[PII-hash]**; `RequestedByDisplayNameSnapshot string(255) Null` **[PII-scrub]**; `Position int Index`; `Status string(20) Index` (`queued`\|`playing`\|`waiting`\|`retrying`\|`played`\|`skipped`\|`rejected` [VC:enum]); `RejectionReason string(100) Null`; `RetryCount int` (default 0); `FailureReason string(100) Null`; `NextRetryAt timestamp Null`; `PriorityBand string(20) Index` (`bump`\|`auto_bump`\|`normal` [VC:enum]; default `normal`) — the three-band ordering band (`Position` orders within a band; bands stack bump→auto_bump→normal, §3.8); `BumpSource string(20) Null` (`raffle`\|`command`\|`redeem` [VC:enum]; null unless `PriorityBand=bump`) — how the item reached the bump band, for display/analytics; `CatalogPurchaseId bigint FK→CatalogPurchases Null` (paid priority / extra-slot / raffle entry); `RequestedAt timestamp`; `PlayedAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: each queued track with requester attribution + lifecycle. `waiting` = provider/environment unavailable (Spotify not open / no device) — indefinite, skipped by the playable-head rule, never auto-removed, no retry consumed; `retrying` = per-item transient error on a healthy provider — bounded exponential backoff (`RetryCount`/`NextRetryAt`, ~3 attempts) before removal. (Single canonical SR item — resolves the Economy `SongRequestHistory` / Integrations `SongRequestItem` overlap; history = this table filtered by terminal `Status`.)

### L.6 SongRequestTrustScores
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `RequesterUserId guid FK→Users Index`; `RequesterTwitchUserId string(50) Index` **[PII-hash]**; `Score decimal(8,4)`; `TotalRequests int`; `PlayedCount int`; `SkippedCount int`; `RejectedCount int`; `IsBlocked bool Index`; `LastRequestAt timestamp Null`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, RequesterUserId)`. Purpose: per-requester trust to gate/prioritize; rebuildable from history.

### L.7 SongRequestRaffles `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `QueueId guid FK→SongRequestQueues Index`; `Status string(20) Index` (`open`\|`drawn`\|`cancelled` [VC:enum]; default `open`); `EntryCostSnapshot int`; `TicketsPerUserSnapshot int`; `WinnerCountSnapshot int`; `Trigger string(20)` (`manual`\|`auto` [VC:enum]); `OpenedAt timestamp`; `DrawnAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId) WHERE Status='open'` (one open raffle per channel; partial index). Purpose: a single song-bump raffle round per channel — winners' songs move into the bump band (L.5 `PriorityBand=bump`, `BumpSource=raffle`); the `*Snapshot` columns freeze the L.4 config at open so an in-flight raffle is unaffected by later config edits.

### L.8 SongRequestRaffleEntries `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `RaffleId guid FK→SongRequestRaffles Index`; `EntrantUserId guid FK→Users Index`; `EntrantTwitchUserId string(50) Index` **[PII-hash]**; `EntrantDisplayNameSnapshot string(255) Null` **[PII-scrub]**; `TicketCount int` (default 1; ≤ `SongRequestRaffles.TicketsPerUserSnapshot`); `CatalogPurchaseId bigint FK→CatalogPurchases Null` (the channel-point debit, when `EntryCostSnapshot > 0`; mirrors L.5); `IsWinner bool Index` (default false; set on draw); `CreatedAt Index`.
**Unique** `(RaffleId, EntrantUserId)`; **Index** `(BroadcasterId, EntrantUserId)` (erasure scrub). Purpose: one entry per (raffle, user) carrying the ticket count + the channel-point purchase link; winners flagged at draw.

### L.9 SongRequestBumpTokens
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `UserId guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `TokenCount int` (default 0); `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, UserId)`. Purpose: per-channel per-user bump-token balance granted when a raffle winner has no queued song; consumed automatically on the winner's next request to place it in the bump band (L.5 `PriorityBand=bump`, `BumpSource=raffle`).

### L.10 MediaShareConfigs `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique**` (one per channel); `IsEnabled bool`; `RequireApproval bool` (default true — submissions enter `pending`); `AllowTwitchClips bool` (default true); `AllowYouTube bool` (default true); `MaxDurationSeconds int` (default 180 — hard cap, tier-scaled); `EntryCost bigint Null` (loyalty points; null/0 = free); `EligibilityJson text Null` **[VC:JSON]** (sub-only / min-standing / min-account-age); `MaxQueueLength int` (default 20); `PerUserCooldownSeconds int` (default 60); `ConfigSchemaVersion int`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId)`. Purpose: per-channel media-share (viewer clip/video queue) config — closed source set (Twitch clips + YouTube), safe-by-default approval, hard duration cap, optional cost + eligibility (owner `media-share.md`).

### L.11 MediaShareRequests `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `RequesterUserId guid FK→Users Index`; `RequesterTwitchUserId string(50)` **[PII-hash]**; `SourceType string(20)` **[VC:enum]** (`twitch_clip`\|`youtube`); `SourceUrl string(2048)`; `MediaRef string(255)` (clip slug / YouTube id); `Title string(300) Null`; `DurationSeconds int`; `ThumbnailUrl string(2048) Null`; `Status string(20) Index` **[VC:enum]** (`pending`\|`approved`\|`rejected`\|`playing`\|`played`\|`skipped`); `QueuePosition int Null`; `EntryCostLedgerEntryId bigint Null`; `RequestedAt timestamp`; `DecidedAt timestamp Null`; `DecidedByUserId guid Null`; `CreatedAt/UpdatedAt/DeletedAt`.
**Index** `(BroadcasterId, Status, QueuePosition)`. Purpose: each submitted clip/video with requester attribution + approval/playback lifecycle; the overlay pulls the next approved item in `(Status, QueuePosition)` order (owner `media-share.md`).

---

## DOMAIN M — Analytics

### M.1 ViewerProfiles `[soft-delete]` — anonymization anchor
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `ViewerUserId guid FK→Users Index`; `ViewerTwitchUserId string(50) Index` **[PII-hash]**; `UsernameSnapshot string(255) Null` **[PII-scrub]**; `DisplayNameSnapshot string(255) Null` **[PII-scrub]**; `FirstSeenAt timestamp Null`; `LastSeenAt timestamp Null Index`; `TotalWatchSeconds bigint`; `TotalMessages bigint`; `TotalCommandsUsed bigint`; `TotalRedemptions bigint`; `TotalSongRequests bigint`; `IsFollower bool`; `IsSubscriber bool`; `SubTier string(10) Null`; `IsAnalyticsOptedOut bool`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId, ViewerUserId)`. Purpose: aggregate per-viewer-per-channel profile + the FK root for detailed tracking.

### M.2 WatchSessions `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `ViewerProfileId guid FK→ViewerProfiles Index`; `ViewerUserId guid FK→Users Index` **[PII via id]**; `StreamId guid FK→Streams Null Index`; `StartedAt timestamp`; `EndedAt timestamp Null`; `DurationSeconds bigint`; `PresenceConfirmed bool`; `MessageCountInSession int`; `CreatedAt Index`.
**Index** `(BroadcasterId, ViewerUserId)` (erasure scrub + per-user watch history). Purpose: per-stream watch intervals with presence verification (anti-AFK basis for watch-time earning).

### M.3 WatchStreaks
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `UserId guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `UserDisplayNameSnapshot string(255) Null` **[PII-scrub]**; `CurrentStreak int`; `MaxStreak int`; `LastSeenDate date`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, UserId)`. Purpose: consecutive-stream attendance streak (existing entity, surrogate-keyed + unique).

### M.4 MessageActivityDaily
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `ViewerProfileId guid FK→ViewerProfiles Index`; `ViewerUserId guid FK→Users Index` **[PII via id]**; `ActivityDate date Index`; `MessageCount int` (counts only, no content); `FirstMessageAt timestamp Null`; `LastMessageAt timestamp Null`.
**Unique** `(BroadcasterId, ViewerUserId, ActivityDate)`. Purpose: per-viewer daily message-count aggregate (data minimization).

### M.5 CommandUsage `[APPEND-ONLY]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `CommandId guid FK→Commands Null Index`; `CommandNameSnapshot string(100)`; `ViewerProfileId guid FK→ViewerProfiles Index`; `ViewerUserId guid FK→Users Index` **[PII via id]**; `ArgsSnapshot string(500) Null` **[PII-scrub]**; `WasSuccessful bool`; `CreatedAt Index`.
**Index** `(BroadcasterId, ViewerUserId)` (erasure scrub of `ArgsSnapshot`). Purpose: per-invocation command usage for analytics + `TotalCommandsUsed`.

### M.6 SongRequestHistory — **REMOVED (deduped).** Use `SongRequestItems` (L.5) filtered by terminal `Status` for `TotalSongRequests`. (Resolves the Economy/Integrations overlap.)

### M.7 ViewerEngagementDaily
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `ViewerProfileId guid FK→ViewerProfiles Index`; `ViewerUserId guid FK→Users Index` **[PII via id]**; `ActivityDate date Index`; `WatchSeconds bigint`; `MessageCount int`; `CommandCount int`; `RedemptionCount int`; `SongRequestCount int`; `CurrencyEarned bigint`; `CurrencySpent bigint`; `GamesPlayed int`.
**Unique** `(BroadcasterId, ViewerUserId, ActivityDate)`. Purpose: per-viewer-per-day roll-up powering charts/time-series without scanning raw logs.

### M.8 ChannelAnalyticsDaily (no PII)
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `ActivityDate date Index`; `UniqueChatters int`; `TotalMessages bigint`; `TotalWatchSeconds bigint`; `NewFollowers int`; `NewSubscribers int`; `BitsCheered bigint`; `CommandsRun bigint`; `RedemptionsCount bigint`; `SongRequests int`; `CurrencyEarnedTotal bigint`; `CurrencySpentTotal bigint`; `GamesPlayed int`; `PeakViewers int Null`.
**Unique** `(BroadcasterId, ActivityDate)`. Purpose: channel-level daily aggregate (pure counts) — survives any viewer erasure.

---

## DOMAIN N — Billing & Monetization

### N.1 BillingTier `[GLOBAL]`
`Id guid PK`; `Key string(20) Unique` (`free`\|`base`\|`pro`\|`premium` [VC:enum]); `DisplayName string(50)`; `PriceCents int`; `Currency string(3)`; `StripePriceId string(255) Null`; `StripeProductId string(255) Null`; `AllowsCustomBotName bool`; `PrioritySupport bool`; `IsPublic bool`; `SortOrder int`; `CreatedAt/UpdatedAt`.
Purpose: tier catalog + commercial attributes (Stripe mapping, premium gates). **Hosted/SaaS is paid-only — there is no free hosted tier.** The seeded **public** hosted plans are `base` (`399` cents), `pro` (`799`), `premium` (`1499`), all `IsPublic=true`. The `free` row is seeded `PriceCents=0`, `IsPublic=false` and exists **only** as the internal marker for self-host / unbilled installs — it is never a cloud plan and SaaS signup can never land on it. `AllowsCustomBotName` is **true for `pro`+ only** (`base` uses the shared platform bot; self-host always allows a custom bot identity). Seed values are reference data (`spec/monetization-billing.md` §10).

### N.2 TierLimit `[GLOBAL]`
`Id guid PK`; `TierId guid FK→BillingTier Index`; `LimitKey string(50) Index` (sandbox_exec_ms/widget_count/asset_storage_mb/queue_size/request_quota_per_day/response_variations_per_trigger/custom_commands/timers/event_responses/worker_concurrency/rate_api_per_min/rate_command_per_min/rate_webhook_in_per_min/rate_song_request_per_min/tts_max_characters [VC:enum]); `LimitValue bigint` (-1 = unlimited).
**Unique** `(TierId, LimitKey)`. Purpose: per-tier quotas around real cost drivers + authoring-count caps (`response_variations_per_trigger`, `custom_commands`, `timers`, `event_responses` — meter quantity, never template expressiveness; self-host resolves all to -1). Every limit is a **safety baseline plus tier-scaled headroom** (`base` < `pro` < `premium`; `scaling-qos.md` §0 D11); the sandbox-budget quota (`sandbox_exec_ms`) and the rate/concurrency keys (`worker_concurrency`, `rate_*`) are **tier-scaled** the same way. Seeded by `DataSeeder` for the **three hosted tiers `base`/`pro`/`premium` only** — there is no hosted `free` tier, so no `free` `TierLimit` rows are seeded; **self-host receives no rows and resolves every limit to `-1`**. See `spec/monetization-billing.md` §8 / §10.

### N.3 Subscriptions `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique** Index`; `TierId guid FK→BillingTier Index`; `Status string(20) Index` (active/trialing/past_due/canceled/incomplete [VC:enum]); `StripeCustomerIdCipher string(512) Null` **[PII-shred]**; `StripeSubscriptionId string(255) Null Index`; `BillingEmailCipher string(512) Null` **[PII-shred]**; `SubjectKeyId guid FK→CryptoKey Null` (DEK for billing PII); `CurrentPeriodStart timestamp Null`; `CurrentPeriodEnd timestamp Null`; `TrialEndsAt timestamp Null` (drives `trialing`→active/past_due); `GracePeriodEndsAt timestamp Null` (drives `past_due` dunning/grace transitions from Stripe webhooks); `CancelAtPeriodEnd bool`; `CanceledAt timestamp Null`; `IsInviteOnlyGrant bool`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(BroadcasterId)`. Purpose: active subscription state per tenant — **supersedes** the interim int-keyed `ChannelSubscription`, surrogate-keyed + GDPR-safe. (Streamer billing — distinct from viewer `TwitchSubscribers`, F.2.)

### N.4 Invoice
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `SubscriptionId guid FK→Subscriptions Index`; `StripeInvoiceId string(255) Unique Null`; `Number string(50) Null`; `Status string(20) Index` (draft/open/paid/void/uncollectible [VC:enum]); `AmountDueCents int`; `AmountPaidCents int`; `Currency string(3)`; `PeriodStart timestamp Null`; `PeriodEnd timestamp Null`; `HostedInvoiceUrl string(2048) Null`; `IssuedAt timestamp Index`; `PaidAt timestamp Null`; `CreatedAt/UpdatedAt`.
**Unique** `StripeInvoiceId`. Purpose: persisted invoices synced from Stripe (deduped) for history/billing page.

### N.5 UsageRecord (billing) `[APPEND-ONLY semantics on Quantity windows]`
`Id bigint PK`; `BroadcasterId guid FK→Channels Index`; `MetricKey string(50) Index` (matches `TierLimit.LimitKey` [VC:enum]); `Quantity bigint`; `PeriodStart timestamp Index`; `PeriodEnd timestamp`; `ReportedToStripe bool`; `CreatedAt`.
**Unique** `(BroadcasterId, MetricKey, PeriodStart)`. Purpose: metered usage per tenant vs tier limits → quota enforcement + overage billing. **`MetricKey` covers `sandbox_exec_ms`** (matching `TierLimit.sandbox_exec_ms`) — sandbox execution time is metered here, so the `TierLimit` exec-ms quota has a usage counterpart. (Distinct from `TtsUsageRecords`, Domain P — that is TTS-character cost accounting.)

### N.6 FoundersBadge
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique** Index`; `GrantedAt timestamp`; `InviteCode string(50) Null Index`; `IsActive bool`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId)`. Purpose: cosmetic founders perk decoupled from subscription so it persists across tier changes.

### N.7 InviteCode
`Id guid PK`; `Code string(50) Unique Index`; `MaxRedemptions int`; `RedemptionCount int`; `GrantsFoundersBadge bool`; `GrantsTierId guid FK→BillingTier Null`; `ExpiresAt timestamp Null`; `CreatedAt/UpdatedAt`.
**Unique** `Code`. Purpose: invite-only launch onboarding; redemptions link to badge/tier grants.

---

## DOMAIN O — Event Store, GDPR/Compliance & Audit

### O.1 EventJournal `[APPEND-ONLY]` — source of truth
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | bigint | PK | Global append order. |
| EventId | guid | Unique | Idempotent dedupe (EventSub/domain id). |
| BroadcasterId | guid | FK→Channels, Null, Index | Tenant; null = platform-global. |
| StreamPosition | bigint | Unique-with-BroadcasterId | Per-tenant monotonic sequence; **app-assigned under the per-tenant lock via `TenantSequences` (Q.3), NOT a DB sequence** (§1.4). Drives projections/replay; uniqueness makes replay idempotent. |
| EventType | string(150) | Index | `channel.chat.message`, `economy.balance.credited`,… |
| EventVersion | int | | Upcaster anchor. |
| Source | string(30) | | `eventsub`\|`domain`\|`irc`\|`import`\|`federation`\|`webhook`. [VC:enum]. (`webhook` = a verified third-party inbound webhook, `webhooks.md` §3.2.) |
| Payload | text | | **[VC:JSON]** ids/refs; raw PII avoided. |
| PayloadIsEncrypted | bool | | True when body holds PII under a DEK. |
| SubjectKeyId | guid | FK→CryptoKey, Null, Index | **Primary/single-subject** DEK encrypting PII in Payload. For multi-subject events (gift sub: gifter+recipient; raid: raider+raided) the full DEK set is `EventSubjectKeys` (O.1a) — erasing one subject shreds via that link, not via this single column. |
| CorrelationId | guid | Index, Null | |
| CausationId | guid | Null | |
| ActorUserId | guid | FK→Users, Null, Index | Internal surrogate of actor. |
| ActorTwitchUserId | string(50) | Null | **[PII-hash]** (when present). |
| Metadata | text | | **[VC:JSON]** headers/trace. |
| OccurredAt | timestamp | Index | Domain time. |
| RecordedAt | timestamp | | Ingest time. |

**Unique** `EventId`, **Unique** `(BroadcasterId, StreamPosition)` (was Index — must be UNIQUE so idempotent replay can't double-apply a position). Purpose: immutable, ordered, per-tenant journal — all read models/projections derive and replay from it; immutability reconciled with GDPR via per-subject crypto-shred. (Single canonical journal — the Content set's `EventJournal` and the Platform set's `EventJournal` are unified here; `TwitchChannelEventLog` (F.4) is its dashboard read-model.)

### O.1a EventSubjectKeys — multi-subject event→DEK link
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| EventId | guid | FK→EventJournal.EventId, Index | The multi-subject event (FK target is Unique). |
| BroadcasterId | guid | FK→Channels, Null, Index | Denormalized for RLS. |
| SubjectIdHash | string(64) | Index | Hashed Twitch id of this subject. |
| SubjectKeyId | guid | FK→CryptoKey, Index | DEK encrypting **this subject's** PII slice of the payload. |
| Role | string(20) | Null | `gifter`\|`recipient`\|`raider`\|`raided`\|… for clarity. |
| CreatedAt | timestamp | | |

**Unique** `(EventId, SubjectKeyId)`. Purpose: closes the real GDPR hole — an event touching two subjects (gift sub, raid) links each subject's DEK here, so erasing one subject shreds only their slice while the event stays replayable. A single `EventJournal.SubjectKeyId` could only shred one subject; this link covers the rest.

### O.2 EventSnapshot
`Id bigint PK`; `BroadcasterId guid FK→Channels Null Index`; `AggregateType string(100) Index`; `AggregateId string(100) Index`; `StreamPosition bigint`; `SnapshotVersion int`; `State text` **[VC:JSON]**; `StateIsEncrypted bool`; `SubjectKeyId guid FK→CryptoKey Null`; `CreatedAt`.
**Unique** `(BroadcasterId, AggregateType, AggregateId)`. Purpose: folded checkpoint so replay needn't start at zero.

### O.3 ProjectionCheckpoint
`Id bigint PK`; `ProjectionName string(150) Index`; `BroadcasterId guid FK→Channels Null Index`; `LastPosition bigint`; `Status string(20)` (running/rebuilding/faulted/paused [VC:enum]); `LastError text Null`; `LastProcessedAt timestamp Null`; `UpdatedAt`.
**Unique** `(ProjectionName, BroadcasterId)`. Purpose: per-projection consume cursor for independent replay/backfill.

### O.4 IdempotencyKey
`Id bigint PK`; `Scope string(100) Index`; `Key string(255)`; `BroadcasterId guid FK→Channels Null Index`; `ResultHash string(64) Null`; `ExpiresAt timestamp Index`; `CreatedAt`.
**Unique** `(Scope, Key, BroadcasterId)`. Purpose: at-most-once guard for events/webhooks/mutating requests.

### O.5 ConsentRecords `[soft-delete-equivalent via Status]`
| Name | Type | Key/Null/Index | Notes |
|---|---|---|---|
| Id | guid | PK | |
| BroadcasterId | guid | FK→Channels, Null, Index | Null = platform-wide ToS. |
| SubjectUserId | guid | FK→Users, Index | Surrogate subject (cascade-safe). |
| SubjectKeyId | guid | FK→CryptoKey, Index | Per-subject DEK. |
| SubjectIdHash | string(64) | Index | Hashed Twitch id (survives anonymization). |
| ConsentType | string(50) | Index | `tos_privacy`\|`age_18_gambling`\|`pronoun_special_category`\|`leaderboard_opt_in`\|`marketing`. [VC:enum]. |
| Status | string(20) | Index | `granted`\|`withdrawn`\|`expired`. [VC:enum]. |
| LawfulBasis | string(30) | | `consent`\|`contract`\|`legitimate_interest`. [VC:enum]. |
| ConsentVersion | string(20) | Null | |
| Source | string(50) | Null | |
| IpAddressCipher | string(255) | Null | **[PII-shred]** proof-of-consent IP. |
| GrantedAt | timestamp | | |
| WithdrawnAt | timestamp | Null | |
| ExpiresAt | timestamp | Null | |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `(BroadcasterId, SubjectUserId, ConsentType)` (one active consent row per subject per type — the 18+ gambling gate reads "the" consent deterministically). Purpose: authoritative consent/lawful-basis ledger (18+ gambling, special-category pronouns, ToS). `ViewerAgeConsents` (K.8) is its fast economy-side cache.

### O.6 ErasureRequest
`Id guid PK`; `SubjectUserId guid FK→Users Index`; `SubjectKeyId guid FK→CryptoKey Index`; `SubjectIdHash string(64) Index`; `BroadcasterId guid FK→Channels Null Index`; `RequestType string(20)` (`erasure`\|`export`\|`opt_out` [VC:enum]); `RequestedBy string(20)` (`self_service`\|`broadcaster`\|`platform_iam` [VC:enum]); `Status string(20) Index` (pending/running/completed/failed/cancelled [VC:enum]); `Scope string(20)` (`deployment`\|`instance`\|`channel` [VC:enum]); `CryptoShredApplied bool`; `AnonymizationApplied bool`; `ExportLocation string(2048) Null`; `ExportFormat string(20) Null`; `RowsAffected int`; `FailureReason text Null`; `RequestedAt timestamp Index`; `CompletedAt timestamp Null`; `CreatedAt/UpdatedAt`.
Purpose: lifecycle of erasure/export/opt-out requests driving the self-service my-data page.

### O.8 ModerationAuditLog `[APPEND-ONLY]`
> Distinct from `ModerationActions` (J.2): J.2 is the operational action record; this is the cross-cutting accountability/justification trail (incl. Plane-C cross-tenant). Kept separate per the Content set's design.

`Id bigint PK`; `BroadcasterId guid FK→Channels Null Index`; `ModerationActionId bigint FK→ModerationActions Null Index`; `ActorUserId guid FK→Users Null Index`; `ActorIamPrincipalId guid FK→IamPrincipals Null` (staff cross-tenant); `EventType string(40) Index` (`action_taken`\|`action_reverted`\|`queue_resolved`\|`cross_tenant_access` [VC:enum]); `Justification string(500) Null`; `MetadataJson text Null` **[VC:JSON]**; `CreatedAt Index`.
Purpose: immutable who/what/when/why spanning channel mod actions + privileged access.

### O.9 IamAuditLog `[APPEND-ONLY]`
`Id bigint PK`; `PrincipalId guid FK→IamPrincipals Index`; `PrincipalType string(20)` [VC:enum]; `Permission string(60) Index`; `TargetBroadcasterId guid FK→Channels Null Index`; `TargetResource string(150) Null`; `Justification text Null`; `BreakGlass bool`; `Outcome string(20) Index` (`allowed`\|`denied` [VC:enum]); `SourceIpCipher string(255) Null` **[PII-shred]**; `OccurredAt timestamp Index`; `CreatedAt`.
Purpose: Plane-C least-privilege accountability (cross-tenant/privileged operator actions, allow/deny, break-glass). (Single canonical IAM audit — unifies the Identity set's `IamAccessAuditLogs` and the Platform set's `IamAuditLog`.)

### O.10 ComplianceAuditLog `[APPEND-ONLY]` — supersedes `DeletionAuditLog`
`Id bigint PK`; `RequestType string(20) Index` (`erasure`\|`export`\|`consent_change` [VC:enum]); `ErasureRequestId guid FK→ErasureRequest Null Index`; `SubjectIdHash string(64) Index`; `BroadcasterId guid FK→Channels Null Index`; `RequestedBy string(20)` (`self_service`\|`broadcaster`\|`platform_iam`\|`system` [VC:enum]); `TablesAffected text` **[VC:JSON]** `List<string>`; `RowsAffected int`; `KeysShredded int`; `Outcome string(20)` (`completed`\|`partial`\|`failed` [VC:enum]); `CompletedAt timestamp`; `CreatedAt`.
Purpose: dedicated audit trail for erasure/export/consent — extends/replaces the existing `DeletionAuditLog`.

### O.11 CommandLogEntry `[APPEND-ONLY, monotonic]` — log-first intake/work queue
`Id bigint PK`; `BroadcasterId guid FK→Channels Null Index` (tenant; null = platform op); `Lane string(10)` (`critical`\|`standard`\|`background` [VC:enum]); `Kind string(40)` (`chat_command`\|`eventsub`\|`api_mutation`\|`webhook_in`\|`scheduled`); `PayloadJson text` **[VC:JSON]**; `SourceEventId guid FK→EventJournal.EventId Null Index`; `IdempotencyKey string(120) Null`; `Status string(12)` (`pending`\|`claimed`\|`done`\|`failed`\|`dead` [VC:enum]); `Attempts int`; `ClaimedByNode string(64) Null`; `ClaimedAt timestamp Null`; `VisibleAt timestamp Index` (retry/backoff visibility); `CreatedAt timestamp`.
**Indexes** `(Status, Lane, VisibleAt)` (fair claim scan), `(BroadcasterId, Status)` (per-tenant in-flight count); **partial-Unique** `(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL`. **Range-partitioned monthly on `CreatedAt`** (SaaS Postgres). Purpose: durable log-first intake — every authorized inbound action is appended here (O(1)) then drained by fair workers (`spec/scaling-qos.md` §2). The **intake/work** log; distinct from `EventJournal` (O.1, the **outcome/fact** log for replay/projection). Pruned by retention after `done`.

---

## DOMAIN P — TTS, Widgets & Platform Config

### P.1 TtsConfig (per-channel)
`Id guid PK`; `BroadcasterId guid FK→Channels **Unique**`; `IsEnabled bool`; `Mode string(20)` (`client_edge`\|`byok`\|`self_host` [VC:enum]); `DefaultProvider string(20)` (`edge`\|`azure`\|`elevenlabs` [VC:enum]); `DefaultVoiceId string(255) Null` (→`TtsVoice.Id`); `ProfanityCensorEnabled bool`; `ModApprovalRequired bool`; `MinBitsToTts int Null`; `MaxCharacters int`; `AzureApiKeyCipher text Null` **[PII-shred]** (AEAD ciphertext, base64); `AzureApiKeyNonce string(64) Null` (AEAD IV); `AzureApiKeyKeyVersion int Null` (DEK version bound into AAD); `AzureRegion string(50) Null`; `ElevenLabsApiKeyCipher text Null` **[PII-shred]** (AEAD ciphertext, base64); `ElevenLabsApiKeyNonce string(64) Null` (AEAD IV); `ElevenLabsApiKeyKeyVersion int Null` (DEK version bound into AAD); `SubjectKeyId guid FK→CryptoKey Null` (BYOK DEK); `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId)`. **AAD** for each BYOK key = `tenantId|provider|tokenType|keyVersion` (e.g. `{BroadcasterId}|azure|byok|{AzureApiKeyKeyVersion}`), matching the `IntegrationTokens` (E.2) envelope so a key swap can't be replayed against a stale tenant/provider. Purpose: channel TTS behavior, provider selection, BYOK keys held under the same AEAD envelope pattern (Cipher + Nonce + KeyVersion, O(1) crypto-shred via the per-tenant DEK), safety toggles.

### P.1a TtsApprovalQueueEntry `[soft-delete]` — mod-approval queue
`Id guid PK`; `BroadcasterId guid FK→Channels Index` (`ITenantScoped`); `RequestedByUserId guid FK→Users Index`; `RequestedByTwitchUserId string(50) Index` **[PII-hash]**; `RequestedByDisplayName string(255) Null` **[PII-scrub]**; `OriginalText text` **[PII-scrub]**; `CensoredText text Null` **[PII-scrub]**; `VoiceId string(255)`; `Provider string(20)`; `Status string(20) Index` (`pending`\|`approved`\|`rejected`\|`expired` [VC:enum]); `WasCensored bool`; `ReviewedByUserId guid FK→Users Null`; `ReviewedAt timestamp Null`; `SourceMessageId string(255) Null`; `StreamId guid FK→Streams Null Index`; `ExpiresAt timestamp Index` (auto-expire stale entries; default queued + 10 min); `CreatedAt/UpdatedAt/DeletedAt`.
**Index** `(BroadcasterId, Status, CreatedAt)`. Purpose: cautious-streamer pre-speak review gate; one row per pending utterance. Default-deny gate when `TtsConfig.ModApprovalRequired` is on (see `tts.md` §1).

### P.2 TtsVoice `[GLOBAL, seed]`
`Id string(255) PK` (external voice id, not PII); `Name string(100)`; `DisplayName string(255)`; `Locale string(10) Index`; `Gender string(10)`; `Provider string(50) Index` (`edge`\|`azure`\|`elevenlabs`); `IsDefault bool`; `CreatedAt/UpdatedAt`.
Purpose: global voice catalog (reference data).

### P.3 UserTtsVoice
`Id guid PK` (was int → surrogate); `BroadcasterId guid FK→Channels Index`; `UserId guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `VoiceId string(255)` (→`TtsVoice.Id`); `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, UserId)`. Purpose: per-viewer TTS voice assignment.

### P.4 TtsUsageRecord `[APPEND-ONLY]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `UserId guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `Provider string(20)`; `VoiceId string(255)`; `CharacterCount int`; `WasCensored bool`; `WasModApproved bool Null`; `StreamId guid FK→Streams Null Index`; `OccurredAt timestamp Index`; `CreatedAt`.
Purpose: per-utterance TTS cost/quota ledger; survives erasure as anonymized aggregate.

### P.5 TtsCacheEntry `[GLOBAL]`
`Id guid PK`; `ContentHash string(64) Unique Index`; `AudioData blob Null`; `StorageRef string(2048) Null` (disk path / object-store key; lets audio move out-of-row — SQLite has no out-of-line blob storage, this avoids page bloat + a later migration); `StorageKind string(20)` (`inline`\|`disk`\|`object_store` [VC:enum]); `SizeBytes int Null`; `DurationMs int`; `Provider string(20)`; `VoiceId string(255)`; `CreatedAt/UpdatedAt`.
**Unique** `ContentHash`. Purpose: content-addressed TTS audio cache (not PII). `AudioData` is nullable: when `StorageKind != inline` the bytes live at `StorageRef`, so large audio never bloats the SQLite page cache.

### P.6 Widget `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(255)`; `Description string(500) Null`; `Framework string(20)` (`vue`\|`react`\|`svelte`\|`vanilla` [VC:enum]); `Source string(20)` (`first_party`\|`verified_gallery`\|`custom` [VC:enum]); `GalleryItemId guid FK→WidgetGalleryItem Null Index`; `ActiveVersionId guid FK→WidgetVersion Null Index`; `EventSubscriptions text Null` **[VC:JSON]**; `Settings text Null` **[VC:JSON]**; `IsEnabled bool`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: configured overlay widget instance; points at served compiled version.

### P.7 WidgetVersion `[APPEND-ONLY]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `WidgetId guid FK→Widget Index`; `VersionNumber int`; `SourceCode text Null`; `CompiledBundle text Null`; `BuildStatus string(20)` (`pending`\|`success`\|`error` [VC:enum]); `BuildError text Null`; `BuildLog text Null`; `ContentHash string(64) Index`; `CompiledAt timestamp Null`; `CreatedAt`.
**Unique** `(WidgetId, VersionNumber)`. Purpose: per-save compiled version + build result; rollback + cache-busted reload.

### P.8 WidgetGalleryItem `[GLOBAL, soft-delete]`
`Id guid PK`; `SubmitterUserId guid FK→Users Null Index` (Null for the platform-owned first-party catalogue); `SubmitterTwitchUserId string(50) Null Index` **[PII-hash]**; `SubmitterDisplayNameSnapshot string(255) Null` **[PII-scrub]**; `Name string(255)`; `Description text Null`; `Framework string(20)`; `TrustTier string(20) Index` (`first_party`\|`verified_community`\|`unverified` [VC:enum]); `SourceKind string(20)` (`in_repo`\|`github` [VC:enum] — first-party catalogue = `in_repo`, community submissions = `github`); `GitHubRepoUrl string(2048) Null` (Null when `SourceKind=in_repo`); `PinnedCommitSha string(40) Null` (Null when `SourceKind=in_repo`); `PinnedTag string(100) Null`; `ReviewStatus string(20) Index` (submitted/in_review/verified/rejected [VC:enum]); `ReviewedByUserId guid FK→Users Null`; `ReviewNotes text Null`; `ReviewedAt timestamp Null`; `AvailableInSaaS bool`; `InstallCount int`; `CreatedAt/UpdatedAt/DeletedAt`.
**Unique** `(GitHubRepoUrl, PinnedCommitSha)` (enforced for `SourceKind=github` rows; in-repo rows hold NULLs, which a partial/filtered unique index excludes). Purpose: curated, reviewed widget catalog (global) — the seeded first-party catalogue (`in_repo`, source compiled in-repo at build time) plus GitHub-pinned community submissions (`github`).

### P.9 WidgetGallerySubmissionEvent `[GLOBAL, APPEND-ONLY]`
`Id guid PK`; `GalleryItemId guid FK→WidgetGalleryItem Index`; `FromStatus string(20) Null`; `ToStatus string(20)`; `ChangedByUserId guid FK→Users Null`; `NewPinnedCommitSha string(40) Null`; `Note text Null`; `OccurredAt timestamp Index`; `CreatedAt`.
Purpose: immutable review/pin-change history for auditability (re-verify on pin update, never auto-pull HEAD).

### P.10 Discord tables (guild link + notifications)
- **DiscordGuildConnection** `[soft-delete]`: `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildId string(50) Index`; `GuildName string(255) Null` **[PII-scrub]**; `BotInstalled bool`; `ServerConsentStatus string(20)` (`pending`\|`approved`\|`revoked`); `ApprovedByDiscordUserId string(50) Null` **[PII-hash]**; `ApprovedAt timestamp Null`; `StreamerEnabled bool`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, GuildId)`. Purpose: both-opt-in handshake (server-approved AND streamer-enabled). Supersedes `DiscordServerAuthorization`.
- **DiscordNotificationConfig** `[soft-delete]`: `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `TriggerType string(30) Index` (`go_live`\|`new_clip`\|`schedule`\|`milestone`); `Enabled bool`; `TargetChannelId string(50)`; `PingRoleId guid FK→DiscordNotificationRole Null Index`; `MessageTemplate text Null`; `EmbedConfig text Null` **[VC:JSON]**; `MilestoneType string(20) Null`; `MilestoneThreshold int Null`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(GuildConnectionId, TriggerType)`.
- **DiscordNotificationRole** `[soft-delete]`: `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `GuildConnectionId guid FK→DiscordGuildConnection Index`; `DiscordRoleId string(50) Index`; `RoleName string(255) Null`; `SelfAssignEnabled bool`; `DmEnabled bool` (default false — this role also DMs its opted-in members on dispatch); `ButtonMessageId string(50) Null`; `ButtonChannelId string(50) Null`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(GuildConnectionId, DiscordRoleId)`.
- **DiscordMemberOptIn** `[soft-delete]`: `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `NotificationRoleId guid FK→DiscordNotificationRole Index`; `DiscordMemberId string(50) Index` **[PII-hash]**; `OptInSource string(20)` (`manual_role`\|`command`\|`button`); `OptedInAt timestamp`; `OptedOutAt timestamp Null`; `DmChannelId string(32) Null` (cached Discord DM channel snowflake, set on first DM); `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(NotificationRoleId, DiscordMemberId)`.
- **DiscordNotificationDispatch** `[APPEND-ONLY]`: `Id guid PK`; `BroadcasterId guid FK→Channels Index`; `NotificationConfigId guid FK→DiscordNotificationConfig Index`; `TriggerType string(30)`; `DedupeKey string(255) Index`; `StreamId guid FK→Streams Null Index`; `PostedMessageId string(50) Null`; `Status string(20)` (`sent`\|`failed`\|`skipped_dupe`); `Error text Null`; `DispatchedAt timestamp Index`; `CreatedAt`. **Unique** `(NotificationConfigId, DedupeKey)`. Purpose: one post per go-live + delivery outcome.

### P.11 AppSetting
`Id guid PK`; `BroadcasterId guid FK→Channels Null Index` (null = global); `Category string(50) Index`; `Key string(255) Index`; `Value text Null` **[VC:JSON for structured]**; `ValueType string(20)` (`string`\|`int`\|`bool`\|`json`\|`secret` [VC:enum]; typed read/validation without guessing); `ConfigSchemaVersion int` (default 1; upcast anchor when `ValueType=json`); `SecureValueCipher string(4096) Null` **[PII-shred]**; `IsSecret bool`; `CreatedAt/UpdatedAt`.
**Unique** `(BroadcasterId, Category, Key)`. Purpose: plain-CRUD config (global + per-tenant + secrets) — not journaled (settings stay CRUD). Supersedes the int-keyed `Configuration`.

### P.12 DeploymentProfile `[GLOBAL, single-row]`
`Id guid PK`; `Mode string(20)` (`saas`\|`self_host_lite`\|`self_host_full` [VC:enum]); `WasAutoDetected bool`; `DbProvider string(20)` (`postgres`\|`sqlite`); `CacheProvider string(20)` (`redis`\|`in_memory`); `EventSubTransport string(20)` (`websocket`\|`conduit_webhook`); `CodeExecutor string(20)` (`wasmtime`\|`jint`); `TokenVault string(20)` (`kms_envelope`\|`local_aes`); `ExposureModel string(20)` (`managed_edge`\|`opt_in_tunnel`); `RlsEnabled bool`; `DefaultGuidanceLevel string(20)` (`novice`\|`expert`; deployment **default** only — the live, per-user, adjustable-anytime value is `UserPreferences.GuidanceLevel`, R.2); `InstanceId guid Unique`; `CreatedAt/UpdatedAt`.
Purpose: persists the one boot-time deployment-profile decision + every adapter selected. (Per-user `GuidanceLevel` moved off this global row to `UserPreferences` — onboarding requires it per-user and adjustable anytime.)

### P.13 FeatureFlag `[GLOBAL]` / FeatureFlagOverride
- **FeatureFlag**: `Id guid PK`; `Key string(100) Unique Index`; `Description string(500) Null`; `IsEnabledGlobally bool`; `RolloutPercentage int`; `MinTierId guid FK→BillingTier Null Index` (FK'd source of truth — a renamed tier key can't silently orphan flags); `MinTierKey string(20) Null` (denormalized convenience copy of the FK'd tier's `Key`); `RequiresConsent string(50) Null`; `DeploymentMode string(20) Null` (`saas`\|`self_host`\|null=both); `CreatedAt/UpdatedAt`.
- **FeatureFlagOverride**: `Id guid PK`; `FeatureFlagId guid FK→FeatureFlag Index`; `BroadcasterId guid FK→Channels Index`; `IsEnabled bool`; `Reason string(255) Null`; `ExpiresAt timestamp Null`; `CreatedAt/UpdatedAt`. **Unique** `(FeatureFlagId, BroadcasterId)`.
Purpose: global flag definition (rollout %, tier/consent/deployment gating) + per-tenant overrides.

### P.14 ObsConnections `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Unique` (one per channel); `Mode string(10)` (`direct`\|`bridge` [VC:enum]); `Host string(255) Null` (OBS WebSocket host; default `127.0.0.1`); `Port int Null` (OBS WebSocket port; default 4455); `PasswordCipher text Null` **[PII-shred]** (AEAD-encrypted OBS WebSocket password via `IFieldCipher` — NEVER plaintext; used by both modes — `bridge` delivers it to the authenticated browser-source bridge for its local OBS connection); `BridgeToken string(36) Null Unique` (rotatable; authenticates the browser-source control bridge to `OBSRelayHub` — distinct from `OverlayToken`, higher privilege); `EventSubscriptionsMask int` (default `All` = bits 0–11; high-volume bits 16–19 opt-in); `IsEnabled bool`; `LastConnectedAt timestamp Null`; `LastError string(300) Null`; `CreatedAt/UpdatedAt/DeletedAt`.
Purpose: per-channel OBS WebSocket v5 connection config (owned by `obs-control.md`). Direct connect on self-host; relay over `OBSRelayHub` on SaaS.

### P.15 SupporterConnections `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `SourceKey string(30)` (`streamelements`\|`streamlabs`\|`kofi`\|`patreon`\|`fourthwall`\|`tipeee`\|`treatstream`\|`donordrive`\|`pally`\|`shopify` [VC:enum]); `ConnectionMode string(20)` (`webhook`\|`socket`\|`ws`\|`poll` [VC:enum]); `AuthSecretCipher text Null` **[PII-shred]** (AEAD-encrypted provider secret via `IFieldCipher` — API key / webhook secret / socket token; write-only, NEVER plaintext; null when OAuth-vaulted; manual rotation); `IntegrationConnectionId guid FK→IntegrationConnections Null` (OAuth providers — Patreon/Shopify/TreatStream — resolve tokens from the `integrations-oauth.md` vault); `InboundWebhookEndpointId guid FK→InboundWebhookEndpoints Null` (webhook providers — Ko-fi/Patreon/Fourthwall/Shopify — the linked `webhooks.md` H.10 inbound endpoint); `IsEnabled bool` (default false); `Status string(20) Null` (`connected`\|`disconnected`\|`error`); `LastEventAt timestamp Null`; `LastError string(300) Null`; `ConfigSchemaVersion int`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, SourceKey)`.
Purpose: per-channel+source supporter-ingest connection config (owned by `supporter-events.md`). One row per source; socket/ws/poll sources hold the encrypted secret + live connection health, webhook sources link the reused inbound endpoint, OAuth sources reference the vaulted `IntegrationConnection`.

### P.16 SupporterEvents `[APPEND-ONLY]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `SourceKey string(30)` (`streamelements`\|`streamlabs`\|`kofi`\|`patreon`\|`fourthwall`\|`tipeee`\|`treatstream`\|`donordrive`\|`pally`\|`shopify` [VC:enum]); `Kind string(20)` (`tip`\|`membership`\|`merch`\|`charity` [VC:enum]); `ProviderTransactionId string(120)` (provider transaction id or composite dedup hash where none exists — TreatStream = `sender+receiver+createdAt+message`; dedup key); `SupporterDisplayName string(100) Null` **[PII-scrub]**; `SupporterUserId guid FK→Users Null` (set when the supporter name matches a known user); `AmountMinor long Null` (minor units); `Currency string(3) Null`; `Tier string(50) Null` (membership tier); `Quantity int Null` (months / item count); `ItemsJson text Null` **[VC:JSON]** (merch line-items); `MessageText string(500) Null` **[PII-scrub]**; `IsRecurring bool`; `PayloadJson text` **[VC:JSON]** (normalized raw payload); `ReceivedAt timestamp`; `CreatedAt`. **Unique** `(BroadcasterId, SourceKey, ProviderTransactionId)`; **Index** `(BroadcasterId, ReceivedAt)`, `(BroadcasterId, Kind)`.
Purpose: append-only supporter-event history + idempotent dedup ledger (owned by `supporter-events.md`). The Unique key drops provider replays; `(BroadcasterId, ReceivedAt)` backs history/leaderboard reads, `(BroadcasterId, Kind)` backs per-kind filtering.

### P.17 AutomationApiToken `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(100)` (label); `TokenHash string(64) Unique` (SHA-256 of the secret — the secret itself is NEVER stored; the `RefreshToken` hashing pattern); `TokenPrefix string(16)` (non-secret display id, e.g. `nnzb_ak_AB12`); `ScopesJson text` **[VC:JSON]** (`string[]` ⊆ `invoke`\|`read`\|`events`\|`chat`); `AllowedPipelineIdsJson text Null` **[VC:JSON]** (`Guid[]`; null/empty ⇒ any pipeline when `invoke` granted); `LastUsedAt timestamp Null`; `ExpiresAt timestamp Null` (null ⇒ no expiry); `RevokedAt timestamp Null`; `CreatedByUserId guid FK→Users`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `TokenHash`, `(BroadcasterId, Name)`; **Index** `BroadcasterId`, `TokenPrefix`.
Purpose: per-channel scoped credential for the External Automation API (owned by `automation-api.md`); the secret is stored hashed and shown once on creation, the `TokenPrefix` identifies it in the UI afterward. Scopes are default-deny; `AllowedPipelineIdsJson` optionally narrows `invoke`.

### P.18 SoundClip `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Index`; `Name string(50)` (slug used by `play_sound`); `DisplayName string(100)`; `StorageKey string(200)` (key in `ISoundClipStore`); `MimeType string(40)` (`audio/mpeg`\|`audio/ogg`\|`audio/wav`); `DurationMs int`; `SizeBytes long`; `DefaultVolume int` (0–100, default 80); `IsEnabled bool` (default true); `CreatedByUserId guid FK→Users`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId, Name)`; **Index** `BroadcasterId`.
Purpose: per-channel curated sound-clip library (owned by `sound-system.md`); the audio blob lives in `ISoundClipStore`, played on the overlay audio bus via `IOverlayClient.PlaySound`. The `(BroadcasterId, Name)` Unique backs `play_sound` slug resolution.

### P.19 VtsConnection `[soft-delete]`
`Id guid PK`; `BroadcasterId guid FK→Channels Unique` (one per channel); `Mode string(20)` (`direct`\|`bridge` [VC:enum]); `Endpoint string(200)` (default `ws://localhost:8001`; direct only); `PluginTokenCipher text Null` **[PII]** (AEAD-encrypted VTS auth token via `IFieldCipher`); `BridgeToken string(64) Null` (SaaS relay, rotatable); `EventSubscriptionsMask int` (which `vts_event`s are subscribed); `IsEnabled bool` (default false); `Status string(20)`; `LastConnectedAt timestamp Null`; `CreatedAt/UpdatedAt/DeletedAt`. **Unique** `(BroadcasterId)` (one per channel); **Index** `BroadcasterId`.
Purpose: per-channel VTube Studio connection config (owned by `vtube-studio.md`); direct on self-host, OBS-relay bridge on SaaS, plugin token AEAD.

---

## DOMAIN Q — Crypto Key Registry (crypto-shred linchpin)

### Q.1 CryptoKey `[GLOBAL/tenant mixed]`
| Name | Type | Key/Null/Index | Notes |
|---|---|---|---|
| Id | guid | PK | The DEK reference other tables FK to. |
| KeyScope | string(20) | Index | `tenant`\|`subject`\|`platform`. [VC:enum]. |
| BroadcasterId | guid | FK→Channels, Null, Index | Owning tenant for `tenant`-scope; null otherwise. |
| SubjectIdHash | string(64) | Index, Null | Hashed user id for `subject`-scope DEKs. |
| WrappedKeyMaterial | text | Null | DEK ciphertext **wrapped by KEK** (envelope); never plaintext. |
| KekReference | string(255) | Null | KMS/key-vault KEK id (SaaS) or local-AES ref (self-host). |
| Provider | string(20) | | `kms_envelope`\|`local_aes` (→`DeploymentProfile.TokenVault`). [VC:enum]. |
| Algorithm | string(30) | | e.g. `AES-256-GCM`. |
| Status | string(20) | Index | `active`\|`rotating`\|`destroyed`. **Destroy = crypto-shred (O(1))**. [VC:enum]. |
| DestroyedAt | timestamp | Null, Index | |
| ErasureRequestId | guid | FK→ErasureRequest, Null | Request that destroyed it. |
| KeyVersion | int | | Monotonic DEK version (default `1`, incremented on rotation). Bound into AEAD AAD (`CipherAad.KeyVersion`) so ciphertext can't be replayed under a rotated key (`gdpr-crypto.md` §4.1). |
| RotatedFromKeyId | guid | FK→CryptoKey, Null | Predecessor on rotation. |
| CreatedAt/UpdatedAt | timestamp | | |

Purpose: per-tenant + per-subject DEK metadata (never raw keys). Destroying a row renders ALL ciphertext under it (tokens, email, event PII, audit PII, billing email, BYOK keys) permanently unrecoverable. Referenced by `Users.SubjectKeyId`, `IamPrincipals.SubjectKeyId`, `IntegrationTokens.EncryptionKeyId`, `EventJournal.SubjectKeyId`, `EventSubjectKeys.SubjectKeyId` (multi-subject events), `EventSnapshot.SubjectKeyId`, `ConsentRecords.SubjectKeyId`, `Subscriptions.SubjectKeyId`, `TtsConfig.SubjectKeyId`.

### Q.2 KeyUsageBinding
`Id bigint PK`; `CryptoKeyId guid FK→CryptoKey Index`; `ResourceTable string(100) Index`; `ResourceColumn string(100)`; `BroadcasterId guid FK→Channels Null Index`; `CreatedAt`.
**Unique** `(CryptoKeyId, ResourceTable, ResourceColumn)`. Purpose: inventory of which table/column is encrypted under each DEK — lets erasure/rotation verify and report exactly what a shred renders unreadable (feeds `ComplianceAuditLog.KeysShredded`).

### Q.3 TenantSequences `[tenant]` — app-assigned per-tenant monotonic counters
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| BroadcasterId | guid | FK→Channels, Index | Owning tenant. |
| SequenceName | string(50) | Index | `event_stream_position`\|`currency_ledger_position`\|… |
| NextValue | bigint | | Next value to hand out; incremented in the **same txn** as the consuming insert. |
| UpdatedAt | timestamp | | |

**Unique** `(BroadcasterId, SequenceName)`. Purpose: the portable per-tenant monotonic sequence the spec promised but never defined. DB auto-increment is **global** (and SQLite has no sequences), so monotonic-per-tenant values (`EventJournal.StreamPosition`, `CurrencyLedgerEntries.TenantPosition`) are app-assigned here: read-and-increment `NextValue` under a row lock (`SELECT … FOR UPDATE` on Postgres; `BEGIN IMMEDIATE` on SQLite) serialized by the unique key — no races, no double-position collision.

---

## DOMAIN R — Lookups & Per-User Preferences

### R.1 Pronouns `[GLOBAL, seed]` — kept as a lookup table (NOT an enum)
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7); FK target from `Users.PronounId`. |
| Key | string(20) | Unique, Index | Stable code (e.g. `she_her`, `they_them`, `he_him`, `any`). |
| DisplayName | string(50) | | UI label (e.g. "She/Her"). |
| Subject | string(20) | | Grammar attr: subject form ("she"/"they"). |
| Object | string(20) | | Object form ("her"/"them"). |
| PossessiveDeterminer | string(20) | Null | "her"/"their". |
| PossessivePronoun | string(20) | Null | "hers"/"theirs". |
| Reflexive | string(20) | Null | "herself"/"themself". |
| IsSingular | bool | | Verb agreement for TTS/templating. |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `Key`. Purpose: kept as a normalized lookup (NOT collapsed to a `[VC:enum]`) because TTS/pronunciation templating needs the grammar attributes. `Users.PronounId` FKs here; the selection is still **[PII-S9]** special-category and explicit-consent gated.

### R.2 UserPreferences `[per-user]`
| Name | Type | Key/Null/Index/Unique | Notes |
|---|---|---|---|
| Id | guid | PK | Surrogate (UUIDv7). |
| UserId | guid | FK→Users, Unique, Index | One row per user (not tenant-scoped — a preference follows the person). |
| GuidanceLevel | string(20) | | `novice`\|`expert` [VC:enum]. Per-user, **adjustable anytime** (onboarding requirement). Default seeded from `DeploymentProfile.DefaultGuidanceLevel`. |
| Locale | string(10) | Null | `en`\|`nl`\|… UI language preference. |
| Theme | string(20) | Null | `dark`\|`light`\|`system`. |
| TimezoneOverride | string(50) | Null | |
| ConfigSchemaVersion | int | | Default 1; upcast anchor for `ExtraJson`. |
| ExtraJson | text | Null | **[VC:JSON]** forward-compatible misc preferences. |
| CreatedAt/UpdatedAt | timestamp | | |

**Unique** `UserId`. Purpose: per-user, cross-tenant preferences. Holds `GuidanceLevel` — onboarding requires it per-user and adjustable anytime; it previously lived only on the global `DeploymentProfile`, which can't be per-user.

---

## 4. TENANT-ISOLATION STORY (RLS-ready)

**Tenant-scoped (carry `BroadcasterId guid` → implement `ITenantScoped` → EF global filter + Postgres RLS `USING (BroadcasterId = current_setting('app.tenant_id')::uuid)`):**
Channels (self, `Id`), AuthSessions, ChannelMemberships, ChannelCommunityStandings, ChannelActionOverrides, PermitGrants, ChannelFederationOptIns, IntegrationConnections, IntegrationTokens, ChannelBotAuthorizations, MusicProviderConfig, Streams, StreamPresets, ScheduledStreamChanges, TwitchSubscribers, TwitchFollowers, TwitchChannelEventLog, Rewards, RewardRedemptions, EventSubSubscriptions, ChatMessages, Commands, ChannelBuiltinCommands, CommandCooldownStates, NamedCounters, Quotes, Giveaways, GiveawayEntries, GiveawayWinners, GiveawayCodePools, GiveawayCodes, EngagementConfigs, ViewerEngagementStates, CustomDataSources, ViewerData, Pipelines, PipelineSteps, PipelineStepConditions, PipelineExecutions, CodeScripts, CodeScriptVersions, HttpEgressAllowlist, InstalledBundles, Timers, EventResponses, all `Moderation*`/`User*`/`Chat*`/`AutoMod*`/`ViewerReports`/`ViewerReportEvidence`/`NetworkNukeBatches`/`SharedBanSettings`/`SharedBanTrustedChannels`, ModerationEscalationPolicies, ModerationEscalationStates, CurrencyConfig, EarningRules, CurrencyAccounts, CurrencyLedgerEntries, GameConfigs, ViewerAgeConsents, GamePlays, GameSessions, CatalogItems, CatalogPurchases, Leaderboard*, SongRequest*, MediaShareConfigs, MediaShareRequests, all Analytics (M.1–M.8), TtsConfig, UserTtsVoice, TtsUsageRecord, Widget, WidgetVersion, ObsConnections, VtsConnections, SupporterConnections, SupporterEvents, AutomationApiTokens, SoundClips, all `Discord*`, Subscriptions, Invoice, UsageRecord, FoundersBadge, FeatureFlagOverride, ConsentRecords, ErasureRequest, AppSetting(when non-null), TenantSequences, EventSubjectKeys, and journal/audit tables with nullable `BroadcasterId` (O.1–O.4, O.8–O.10).

**Cross-tenant (membership/trust-predicate RLS, NOT single `BroadcasterId`):** SavingsJars, SavingsJarMemberships, JarContributions — predicate = `JarId ∈ (jars where this tenant has an accepted SavingsJarMembership)`. Federation propagation rows gated by `ChannelFederationOptIns` + `FederationPeers.TrustState`.

**Global (NO `BroadcasterId`, guarded by IAM/trust/seed-reference, not RLS):** Users (subject registry, accessed via tenant-scoped junctions), UserPreferences (per-user, not per-tenant), Pronouns (seed lookup), all `Iam*`, all `Federation*`, ActionDefinitions, IamPermissions/Roles/RolePermissions/Principals/RoleAssignments, BillingTier, TierLimit, FeatureFlag, DeploymentProfile, EventSubConduits, EventSubConduitShards, TtsVoice, TtsCacheEntry, WidgetGalleryItem, WidgetGallerySubmissionEvent, InviteCode, platform-scope CryptoKey, KeyUsageBinding.

**SQLite/self-host:** `RlsEnabled=false` — the EF global query filter is the sole isolation mechanism (single-tenant or trusted self-host; same filter code path, no RLS policies).

---

## 5. GDPR STORY (erasure/anonymization, cascade-safe)

**PII surface (the only columns ever touched on erasure):**
- **[PII-hash]** — every `*TwitchUserId` / `*DiscordMemberId` / `ApprovedByDiscordUserId` / `SubjectTwitchUserId` / `ActorTwitchUserId` (all carry the same Twitch id) → replaced by one consistent deterministic hash in a single transaction.
- **[PII-scrub]** — `Username`+`UsernameNormalized`, `*DisplayNameSnapshot`, `*UsernameSnapshot`, `ChatMessages.Content`, `MessageContentSnapshot`, `UserNotes.Content`, free-text `Reason`/`UserInput`/`InputArgs`/`ArgsSnapshot`/`GuildName` → nulled/tombstoned. Every snapshot-bearing append-only table now carries a `(BroadcasterId, SubjectUserId)` (or `(BroadcasterId, *UserId)`) index so scrub is an indexed lookup per subject, not a full scan — without these the "O(1) erasure" claim was false for `[PII-scrub]` (only `[PII-shred]` is truly O(1)).
- **[PII-shred]** — `EmailCipher`, `IpAddressCipher`, all `*TokenCipher`/`CipherText`, `Azure/ElevenLabsApiKeyCipher`, `Billing/StripeCustomerIdCipher`, `SecureValueCipher`, `EventJournal.Payload` (when encrypted) → made unreadable by destroying the DEK.
- **[PII-S9]** — `Users.PronounId` + `Users.AltPronounId` (FK→`Pronouns` lookup) → nulled (special-category). The `Pronouns` lookup itself is seed data (never PII); only the per-user selection is scrubbed.

**Erasure procedure (O(1), cascade-safe by surrogate keys):**
1. **Anonymize identifiers in place:** hash `Users.TwitchUserId` + every denormalized `*TwitchUserId`; scrub `Username/UsernameNormalized/DisplayName/NickName`, null `PronounId`+`AltPronounId`, and all snapshot columns (found via the per-table `(BroadcasterId, SubjectUserId)` indexes — indexed, not full-scan); set `Users.IsAnonymized=true`. The FK graph (memberships, standings, ledger, moderation, analytics, jars) is keyed on surrogate `Users.Id` → **never touched** → balances, ranks, counts, audit links all survive.
2. **Crypto-shred:** locate the subject's DEKs via `Users.SubjectKeyId` + `EventSubjectKeys` (multi-subject events) + `KeyUsageBinding`; set `CryptoKey.Status=destroyed`, drop `WrappedKeyMaterial`, set `DestroyedAt`. All ciphertext under those DEKs (tokens, email, event PII — including each subject's slice of a shared gift/raid payload — billing email, BYOK keys, IPs) becomes permanently unrecoverable — including backups (key never existed there).
3. **Revoke auth:** revoke all `RefreshTokens`/`AuthSessions` by `UserId`.
4. **Scrub residual PII:** hash/strip IP in `AuthSessions`, `ConsentRecords`, `IamAuditLog`.
5. **Withdraw/record consent:** set `ConsentRecords.Status=withdrawn` where applicable.
6. **Audit the erasure itself:** write `ErasureRequest` (status→completed) + `ComplianceAuditLog` (`SubjectIdHash`, `TablesAffected`, `RowsAffected`, `KeysShredded`) — these are append-only and retain only the **hashed** subject id, never reversible PII.

**Data portability (export):** `ErasureRequest.RequestType=export` produces machine-readable JSON (`ExportFormat=json`) at `ExportLocation`, audited in `ComplianceAuditLog`.

**Retention/minimization:** data is stored **permanently** — there is no auto-purge and no tiered retention horizon. Append-only journals (chat logs, event journal, audit) are **immutable** and never row-deleted. PII is removed **exclusively** via manual crypto-shred erasure-on-request (`ErasureRequest`); journals are scrubbed in place, never purged or row-deleted. Daily aggregates (`ChannelAnalyticsDaily`, `ViewerEngagementDaily`) carry non-PII counts and are retained indefinitely like everything else.

---

## 6. KEY DELTAS FROM CURRENT SCHEMA (load-bearing, do now)

1. `User.Id`/`Channel.Id` (raw-Twitch-id `string(50)` PKs) → **surrogate `guid` PKs**; Twitch ids demoted to indexed `TwitchUserId`/`TwitchChannelId` attributes. Precondition for cascade-safe erasure.
2. **`ITenantScoped.BroadcasterId` widened `string` → `Guid`** (FK→`Channels.Id`). The single interface change that propagates the surrogate-key decision across every tenant table.
3. `Permission` (generic `SubjectType/ResourceType/PermissionValue`) → `ActionDefinitions` + `ChannelActionOverrides` + `PermitGrants`.
4. `ChannelModerator` → `ChannelMemberships` (management ladder) + `ChannelCommunityStandings` (community ladder).
5. `Service` (inline tokens) → `IntegrationConnections` + `IntegrationTokens` + `CryptoKey` (+ Spotify/YouTube config, BotAccounts).
6. `ChannelBotAuthorization`/`DiscordServerAuthorization` (int PKs) → guid + `BotAccounts` link / `DiscordGuildConnection` (both-consent).
7. `ChannelSubscription` (int, streamer billing) → `Subscriptions` (guid, GDPR-safe); viewer subs are the separate `TwitchSubscribers`.
8. `EventSubscription` → `EventSubSubscriptions` (transport/conduit-aware).
9. `Configuration` (int) → `AppSetting` (guid, global+tenant). `DeletionAuditLog` → `ComplianceAuditLog` (+ surrogate-subject FK).
10. Inline pipeline JSON blobs (on Command/Reward/EventResponse) → normalized `Pipelines`/`PipelineSteps`/`PipelineStepConditions` with `PipelineId` FKs.

**Overlaps deduped:** one `EventJournal` (was 2), one `RewardRedemptions` (was Economy + Integrations), one `SongRequestItems` (Economy `SongRequestHistory` dropped → derive from terminal status), one `IamAuditLog` (was `IamAccessAuditLogs` + `IamAuditLog`), one `IntegrationConnections` (was 2), one IAM principal table, `ViewerAgeConsents` folded onto authoritative `ConsentRecords`.

**Audit deltas folded in at lock (2026-06-16):**
11. **7 new tables added:** `EarningRules` (K.1a), `StreamPresets` (F.10) + `ScheduledStreamChanges` (F.11), `EventSubConduits`/`EventSubConduitShards` (F.8/F.9, global), `UserPreferences` (R.2, per-user `GuidanceLevel`), `ChannelBuiltinCommands` (G.2a — built-in command toggles; closes "commands show 0 / seeding skipped"), `NetworkNukeBatches` (J.2a + `ModerationActions.NetworkNukeBatchId` link).
12. **FK-in-JSON-blob → join tables:** `SharedBanSettings.TrustedChannelsJson` → `SharedBanTrustedChannels` (J.9a); `ViewerReports.EvidenceMessageIds` → `ViewerReportEvidence` (J.8a).
13. **`EventJournal (BroadcasterId, StreamPosition)` is now UNIQUE** (was Index) for idempotent replay; per-tenant monotonic positions are app-assigned via `TenantSequences` (Q.3), never DB auto-increment.
14. **`Pronouns` kept as a lookup table** (R.1) with grammar attrs; `Users.PronounId` FKs it — NOT enum-ified.
15. **Portability:** SQLite provider wired by DI adapter; `Microsoft.EntityFrameworkCore.Sqlite` referenced; native `jsonb`/`HasDefaultValueSql` banned, every `[VC:JSON]` is a real converter; `*Normalized` lowercase unique columns on `Users.Username`/`Channels.Name`/`Commands.Name`/`CatalogItems.Name`; `TtsCacheEntry.StorageRef` for out-of-row audio; `ConfigSchemaVersion` on every app-interpreted JSON-config table; `(BroadcasterId, SubjectUserId)` indexes on snapshot tables; `EventSubjectKeys` (O.1a) for multi-subject event shred; `decimal` scores documented as SQLite REAL-affinity.
16. **Lifecycle columns:** `Channels.Status`/`SuspendedAt`/`SuspendedReason` (Plane-C `tenant:suspend` target); `Subscriptions.TrialEndsAt`/`GracePeriodEndsAt`; `Users.IsBot`/`LastSeenAt`; `IntegrationConnections` refresh-health columns; `AppSetting.ValueType`; `FeatureFlag.MinTierId` FK.
17. **Commands/counters/prefix (owner `commands-pipelines.md`):** `NamedCounters` (G.4) persistent cross-command counter store; `Commands.PrefixMode`/`CustomPrefix`/`MatchMode`/`MatchPattern` per-command trigger model (`Regex` is a first-class match mode made ReDoS-safe by .NET's `RegexOptions.NonBacktracking` engine — no sandbox — via `IRegexMatcher`); `Channels.DefaultCommandPrefix` (default `!`) channel-level default. Closes the catalog's named-counters + `bot.prefix`-storage open questions.

> **Grounding correction (was false in the draft):** `Channel.Tags`/`ContentLabels` do **NOT** already use a portable value-converter — `ChannelConfiguration.cs` uses Postgres-native `.HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")`, which breaks on SQLite. `[VC:JSON]` is net-new work on every existing JSON column, and no SQLite provider/dual-migration exists yet (`UseNpgsql` only). Both are now mandated above, not assumed done.

**Grounding (absolute paths):** base types `c:\Projects\NoMercyLabs\nomnomzbot\server\src\NomNomzBot.Domain\Common\{BaseEntity,SoftDeletableEntity,ITenantScoped,ISoftDeletable}.cs`; existing entities to refactor `...\Domain\Entities\{User,Channel,Service,Permission,ChannelModerator,ChannelBotAuthorization,DiscordServerAuthorization,ChannelSubscription,EventSubscription,Configuration,DeletionAuditLog,Stream,Command,Pipeline,EventResponse,ChatMessage,Timer,Reward,TtsVoice,TtsUsageRecord,UserTtsVoice,TtsCacheEntry,Widget,WatchStreak}.cs`; design docs `...\docs\design\2026-06-16-*.md`. Confirmed against source: `ITenantScoped.BroadcasterId` is currently `string`; `User.Id`/`Channel.Id` are `string(50)` Twitch-id PKs; `List<>` columns (`Channel.Tags`) already use the EF JSON-converter pattern this spec mandates.

---

# Appendix — Adversarial Hardening Pass (no-churn audit)

This is a decisive finding. The spec claims `Channel.Tags` "already use the EF JSON-converter pattern this spec mandates" and that there's "no jsonb." The actual code does the opposite: `.HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")` — hard Postgres-native `jsonb` with a Postgres-only default SQL. That is a SQLite-breaking pattern and directly contradicts the spec's grounding claim and its provider-agnostic rule. The current code is NOT provider-agnostic at all.

I have enough ground truth. I've confirmed every load-bearing claim and found two false grounding claims plus the real schema gaps. Let me write the adversarial review.

---

# Adversarial Schema Review — "What Will Force a Migration Later?"

I grounded every load-bearing claim against the real source. Two of the spec's grounding claims are **false**, and they matter because they're the foundation the "do it now" argument rests on. The rest of the hunt follows.

## 0. Ground-truth corrections (the spec misrepresents current state)

- **`Channel.Tags`/`ContentLabels` do NOT use a portable value-converter.** `ChannelConfiguration.cs:51-56` uses `.HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")` — hard Postgres-native `jsonb` plus Postgres-only default SQL. The spec asserts "Confirmed against source: `List<>` columns (`Channel.Tags`) already use the EF JSON-converter pattern this spec mandates." **That is wrong.** The current pattern is exactly the anti-pattern the spec forbids, and it will throw on SQLite today. The `[VC:JSON]` rule is net-new work on **every** existing JSON column, not a "match the existing pattern."
- **There is no SQLite provider and no dual-migration setup.** `DependencyInjection.cs:69` wires `UseNpgsql` only; migrations live in one assembly with `jsonb`/`uuid` types baked in. The "two migration sets generate from one model" claim is aspirational — nothing today produces them. This is the single biggest provider-agnostic risk and it's understated.
- `Channel.Id` is currently a **shared PK with `User.Id`** (`Channel.cs:65` `[ForeignKey(nameof(Id))]`), not a `string(50)` with a separate owner FK. The spec's `Channels.OwnerUserId` unique FK is correct as a target but the migration is bigger than "demote PK to attribute" — it's also "split the shared 1:1 key."
- `Pronoun` is **already a lookup table** (`Pronoun.cs`, `DbSet<Pronoun>`). The spec models `Users.Pronoun` as `string(50) [VC:enum]`. That's a regression from a normalized table to an enum-string — see PII/normalization finding below.

Everything else the spec claims about current state (`BroadcasterId` is `string`, raw-Twitch-id PKs, `Service` holds plaintext tokens, `Permission` generic shape, no query filter/RLS wired) checks out.

---

## A. Provider-agnostic breakers (highest churn risk — these force re-platform migrations)

| # | Table/Area | Gap | Fix |
|---|---|---|---|
| A1 | **All `[VC:JSON]` columns** | The model says "portable text via ValueConverter," but the only existing implementation uses native `jsonb`. If migrations are generated from the current config, SQLite is dead on arrival and you migrate every JSON column later. | Mandate `.HasConversion<T>()` + `ValueComparer` for **every** collection/dict column **now**; ban `HasColumnType("jsonb")` and `HasDefaultValueSql("…::jsonb")` in a config-review gate. Generate a SQLite migration in CI as a provider-parity test before declaring "ship-ready." |
| A2 | **`bigint` identity PKs on journals** | "bigint identity for monotonic ordering" + "monotonic per tenant" (`CurrencyLedgerEntries`, `EventJournal.StreamPosition`). Postgres `IDENTITY`/sequences and SQLite `AUTOINCREMENT` give **global** monotonicity, not per-tenant, and SQLite has no sequences. Per-tenant monotonic `StreamPosition` is application-assigned and will race under concurrency. | Specify `StreamPosition` as an **application-computed** value under a per-tenant advisory lock / `SELECT max+1` in the same txn (or an `EventStreamSequences(BroadcasterId, NextPosition)` row), not a DB sequence. State the concurrency-control mechanism, or you'll migrate it after the first double-position collision. |
| A3 | **`decimal(8,4)` trust/heat scores** | Fine on PG; SQLite stores `decimal` as `TEXT`/`REAL` via EF and **cannot** do correct `ORDER BY`/range on it without the converter. `UserTrustScores.TrustScore Index` + threshold comparisons in AutoMod will silently mis-sort on SQLite. | Either store scores as scaled `int`/`bigint` (e.g. basis points) for portable ordering, or document the SQLite REAL-affinity behavior and accept lossy compare. Decide now. |
| A4 | **`blob` audio cache** (`TtsCacheEntry.AudioData`) | Large `blob` in-row is fine on PG (TOAST) but bloats SQLite page cache and has no out-of-line storage. Not a correctness break, but a "we'll move audio to disk/object-store later" migration waiting to happen. | Add `StorageRef string(2048)` now (path/object-key) and make `AudioData` nullable, so the blob can move out without a schema change. |
| A5 | **Case-insensitive lookups** | Spec says "normalized lowercase column + index (no citext)" but **defines none.** `Users.Username`, `Channels.Name`, `Commands.Name`, `CatalogItems` are looked up case-insensitively in a Twitch context. Without a `UsernameNormalized` column you either use `citext` (banned) or `LOWER()` indexes (PG-only expression index, not SQLite-portable). | Add explicit `*Normalized string(n) Index` columns (e.g. `Channels.NameNormalized`, `Users.UsernameNormalized`, `Commands.NameNormalized`) and make the unique constraints reference them. This is a guaranteed later-migration if omitted. |

---

## B. Missing columns you'll obviously wish you had

| # | Table | Gap | Fix |
|---|---|---|---|
| B1 | **`Users`** | No `IsBot` flag, no `LastSeenAt`. Every viewer-analytics path wants "is this account the bot / a known bot" and "when did we last see this identity globally." `BotAccounts` exists but a viewer `User` row that *is* a bot can't be flagged. | Add `IsBot bool Index`, `LastSeenAt timestamp Null`. |
| B2 | **`Channels`** | No `Status`/lifecycle state (active / suspended / churned / banned-by-platform). `Enabled bool` + `DeletedAt` cannot express "suspended for ToS" vs "owner disabled" vs "soft-deleted." Platform-IAM `tenant:suspend` permission exists with **nothing to write to.** | Add `Status string(20) Index` (`active`\|`suspended`\|`churned`\|`platform_banned`) + `SuspendedAt`, `SuspendedReason`. |
| B3 | **`Subscriptions`** | No `TrialEndsAt`, no `GracePeriodEndsAt`. `Status` has `trialing`/`past_due` but no dates to drive the dunning/grace transitions Stripe webhooks set. You will add these the first week of real billing. | Add `TrialEndsAt timestamp Null`, `GracePeriodEndsAt timestamp Null`. |
| B4 | **`PipelineSteps` / `Commands` / `Timers` / `EventResponses`** | `ConfigJson`/`Messages`/`TemplateResponses` carry no schema version. The moment you change a config shape you have no per-row version to upcast from → a data migration over JSON blobs. The event side has `EventVersion`; the config side has nothing. | Add `ConfigSchemaVersion int` (default 1) to every table holding a `[VC:JSON]` config blob that the app interprets. |
| B5 | **`Widget` / `CodeScripts`** | No `LastError`/`LastRunAt` on the *instance* (versions have build status, but the running widget/script has no "last runtime failure"). Overlay debugging will demand it. | Add `LastRuntimeError text Null`, `LastRanAt timestamp Null` to `Widget` and `CodeScripts`. |
| B6 | **`IntegrationConnections`** | No `LastRefreshAt`/`LastErrorAt`/`FailureCount`. Token-refresh health is operationally essential and `Status` alone (`expired`/`needs_reauth`) can't drive backoff. | Add `LastRefreshedAt`, `LastErrorAt timestamp Null`, `ConsecutiveFailureCount int`. |
| B7 | **`AppSetting`** | No `ValueType` discriminator. A polymorphic `Value text` blob with `IsSecret` but no type tag forces consumers to guess; adding typed validation later is a migration. | Add `ValueType string(20)` (`string`\|`int`\|`bool`\|`json`\|`secret`). |

---

## C. Missing / wrong FKs and join tables

| # | Location | Gap | Fix |
|---|---|---|---|
| C1 | **`SharedBanSettings.TrustedChannelsJson`** + **`SongRequestQueues.ProviderPriority`** + **`LeaderboardConfigs`** | `TrustedChannelsJson text [VC:JSON] List<guid>` is a **set of FKs hidden in a JSON blob.** You cannot FK-enforce, cannot index, cannot join, cannot cascade on channel erasure. This is exactly the "should be a join table" smell. | Replace with `SharedBanTrustedChannels(Id, BroadcasterId FK, TrustedChannelId guid FK→Channels, …)` join table. A trusted-channel relationship is a first-class, query-driven, erasure-cascading entity — not a blob. |
| C2 | **`RewardRedemptions.EventId` / `CurrencyLedgerEntries.EventId`** | Typed as bare `guid Null Index` "correlates to `EventJournal.EventId`" but **not an actual FK** (EventJournal PK is `bigint Id`, `EventId` is a unique `guid`). It's a soft correlation that no constraint protects. Fine *if intentional* (append-only cross-ref), but it's undeclared whether this is enforced. | State explicitly: declare FK→`EventJournal.EventId` (since `EventId` is unique it's a valid FK target) **or** document it as an intentionally-unenforced correlation. Don't leave it ambiguous — that ambiguity becomes a "add the FK" migration. |
| C3 | **`Pronoun` regression** | Spec demotes the existing `Pronoun` lookup table to `Users.Pronoun string(50) [VC:enum]`. You lose `Subject`/`Object`/`Singular` grammar data that `UsernamePronunciation`/TTS templating uses. | Keep `Pronouns` as a lookup table; `Users.PronounId guid FK→Pronouns Null`. Don't enum-ify a table that carries attributes. |
| C4 | **`DiscordNotificationConfig.PingRoleId`** | FK→`DiscordNotificationRole` but a config can plausibly ping **multiple** roles. Single nullable FK locks you to one. | If multi-role ping is foreseeable (it is, for tiered notifications), make it a `DiscordNotificationConfigRoles` join table now. If truly one, document the constraint. |
| C5 | **`FeatureFlag.MinTierKey` / `Channels.BillingTierKey` / `TtsConfig.DefaultVoiceId`** | These reference `BillingTier.Key` and `TtsVoice.Id` by **denormalized string**, not FK. `BillingTierKey` on `Channels` is explicitly "denormalized," fine — but `FeatureFlag.MinTierKey` and `DiscordNotificationConfig` string refs to roles have no integrity guarantee. A renamed tier key silently orphans flags. | For `FeatureFlag.MinTierKey`, use `MinTierId guid FK→BillingTier Null`. Denormalized convenience columns are fine *with* an FK'd source of truth; here there's no FK at all. |

---

## D. Indexes missing on FKs / hot paths / unique constraints

The spec indexes many FKs but is **inconsistent** — it relies on prose "Indexes" lines per table and skips several. Concrete gaps:

| # | Table | Missing index | Why it's a hot path |
|---|---|---|---|
| D1 | **`CurrencyLedgerEntries`** | `(BroadcasterId, AccountId, Id)` composite | Balance = fold over ledger by account. Without a composite, every balance recompute is a scan. Listed indexes are single-column only. |
| D2 | **`ChatMessages`** | `(BroadcasterId, AuthorUserId, CreatedAt)` | Per-user message history in the mod panel + erasure scrub by user. `CreatedAt` alone serves time-range scans, but per-user lookups scan without the composite. |
| D3 | **`EventJournal`** | unique on `(BroadcasterId, StreamPosition)` is declared as **Index, not Unique** | Per-tenant position **must** be unique or projections double-apply. Spec says "Index" — must be a UNIQUE constraint. |
| D4 | **`RefreshTokens`** | index on `(UserId, RevokedAt)` | "Revoke-all-by-user on erasure" is a stated hot path; `UserId` index alone without `RevokedAt` still scans revoked rows. |
| D5 | **All `*Snapshot`/append-only with `BroadcasterId`** | Many APPEND-ONLY tables (`GamePlays`, `CatalogPurchases`, `WatchSessions`, `CommandUsage`) only get `CreatedAt Index` per spec; dashboard queries filter `(BroadcasterId, CreatedAt)`. | Single-column `CreatedAt` index across all tenants is useless for per-tenant time-range scans. Need `(BroadcasterId, CreatedAt)` composites. |
| D6 | **`ConsentRecords`** | unique on `(BroadcasterId, SubjectUserId, ConsentType)` | Nothing prevents duplicate active consent rows of the same type. The 18+ gambling gate reads "the" consent — needs a uniqueness guarantee (or explicit "latest-wins by GrantedAt" documented). |
| D7 | **`IdempotencyKey`** | TTL/cleanup index on `ExpiresAt` is present, good — but no index supporting the actual lookup `(Scope, Key, BroadcasterId)` beyond the unique. Unique covers it. OK. (Noting it's fine to preempt a false flag.) |

**Systemic fix:** add a schema rule — *every FK column and every `(BroadcasterId, <time>)` query path gets a composite index, declared explicitly per table.* The current per-table prose makes omissions invisible.

---

## E. Multi-tenancy isolation gaps

| # | Finding | Fix |
|---|---|---|
| E1 | **`Users` is global with no tenant-junction integrity story for cross-tenant leakage.** A viewer in channel A and channel B is one `Users` row. The spec says "accessed via tenant-scoped junctions," but `ViewerProfiles`, `CurrencyAccounts`, etc. are scoped — **`Users` itself isn't**, and `Users.EmailCipher`/`Pronoun` are global PII readable by any tenant query that joins to `Users`. | Confirm that no tenant-facing query selects `Users.EmailCipher`/global PII; expose viewer data **only** through tenant-scoped projection tables. Add a guard (view or DTO boundary) — this is an isolation hole, not just a modeling note. |
| E2 | **RLS is unbuilt and the model can't be RLS-tested until the `Guid` migration lands.** The entire isolation story depends on a rebuild that hasn't happened. "Ship-ready" cannot be claimed against a tenant model whose isolation mechanism (global query filter) is **not even wired** (`AppDbContext.OnModelCreating` applies zero filters today). | Wire the `ITenantScoped` global query filter in `OnModelCreating` and add RLS policies in the Postgres migration as part of *this* schema delivery, with an isolation test (tenant A cannot read tenant B). Until that test passes, isolation is unproven. |
| E3 | **Cross-tenant `SavingsJars` RLS is described in prose, not modeled.** "Membership-predicate RLS" has no concrete policy and the `JarContributions`/`SavingsJarMemberships` predicate spans tables. This is the single hardest RLS policy and it's hand-waved. | Write the actual policy SQL (or the EF filter expression) now and test it. A membership-based RLS predicate that's wrong leaks pooled currency across channels. |

---

## F. Event-sourcing / GDPR gaps

| # | Finding | Fix |
|---|---|---|
| F1 | **Crypto-shred vs. denormalized plaintext snapshots.** The erasure story relies on crypto-shred for `[PII-shred]` and in-place scrub for `[PII-scrub]`. But `*DisplayNameSnapshot`/`*UsernameSnapshot` are plaintext, denormalized across **~20 tables** (redemptions, ledger, leaderboards, moderation, analytics). Erasure must scrub **all** of them in one txn. The spec lists them but there's **no index** to *find* them by subject on most append-only tables (see D5). Erasure becomes a full-table scan per table. | Add `(BroadcasterId, SubjectTwitchUserId)` or `(SubjectUserId)` indexes on every table carrying a scrubable snapshot, or erasure SLA is unbounded. The GDPR "O(1)" claim is false for `[PII-scrub]` columns — only `[PII-shred]` is O(1). |
| F2 | **`EventJournal.Payload` PII is all-or-nothing per row.** `PayloadIsEncrypted bool` + one `SubjectKeyId`. An event involving **two** subjects (gift sub: gifter + recipient; raid: raider + raided) can only key to one DEK. Erasing one subject can't shred a shared-payload event. | Either guarantee one-subject-per-event (document and enforce), or model `EventSubjectKeys(EventJournalId, SubjectKeyId)` so multi-subject events shred correctly. This is a real GDPR hole for gift/raid events. |
| F3 | **No `EventUpcaster`/version-map persistence.** `EventVersion int` is the "upcaster anchor" but there's no table or declared mechanism recording which upcaster transforms vN→vN+1. Replay across a deployed version boundary is undefined. | Acceptable as code-only (upcasters are code), but **state it** — otherwise someone adds a table later. Document: upcasters are compiled, keyed by `(EventType, EventVersion)`. |
| F4 | **`ComplianceAuditLog.TablesAffected text [VC:JSON] List<string>`** | Same anti-pattern as C1 — a list of table names in a blob. Fine for an audit record (never joined), so this one is acceptable. Flagging only to confirm it's a deliberate exception, not an oversight. |

---

## G. Under/over-normalization & enum-vs-lookup

| # | Finding | Fix |
|---|---|---|
| G1 | **Pervasive `[VC:enum]` string enums that are really lookup data.** `ManagementRole`, `Standing`, `BillingTier.Key`, `GameType`, `EntryType`, dozens more. Most are fine as enums. But several carry **attributes** that want a lookup table: `BillingTier` already is one (good); `GameType` has odds/edge that vary — already in `GameConfigs` (good). **`SubTier` (`1000`/`2000`/`3000`)** appears as a bare string in 4+ tables with no lookup — if Twitch adds a tier or you attach perks per tier, it's a multi-table migration. | Minor: acceptable as-is given Twitch's fixed tiers, but note the risk. The bigger one is **G2.** |
| G2 | **`ActionDefinitions.FloorTier`/`DefaultLevel`/`FloorLevel` as bare ints** with the level→role mapping (10/20/30/40) **denormalized into `ChannelMemberships.LevelValue` and `ChannelCommunityStandings.LevelValue`.** The numeric ladder is encoded in **three** places with no single source. Re-tiering the ladder (insert a level between mod=10 and editor=30) touches every row in two tables. | Introduce a `PermissionLevels(Value int PK, Key string, DisplayName)` lookup as the one source; `LevelValue` columns FK to it. Inserting a rung becomes one row, not a data migration. |
| G3 | **`Storage`/`Record` entities exist in code but are absent from the spec.** `DbSet<Storage>`, `DbSet<Record>` (used by `TimerSchedulerService`). The spec's "key deltas" don't mention them — are they dropped, kept, or replaced? Silent omission = a "where did Records go" migration surprise. | Explicitly account for `Storage` and `Record` in the deltas: keep, fold into `AppSetting`, or drop. Don't leave live tables unaddressed. |

---

## H. Things the spec got right (so you don't re-litigate them)

Surrogate-key strategy, `CryptoKey` registry as shred linchpin, `KeyUsageBinding` inventory, splitting `Permission` into definition/override/permit, single canonical `EventJournal`/`RewardRedemptions`/`SongRequestItems` dedup, append-only-with-CreatedAt-only convention, snapshot columns surviving FK deletion, `ConsentRecords` as authoritative with `ViewerAgeConsents` as cache, transport-aware `EventSubSubscriptions`, and the founders-badge-decoupled-from-subscription call. These are sound and churn-resistant.

---

# VERDICT: **NEEDS-FIXES**

The model is architecturally strong and genuinely thinks ahead on identity, crypto-shred, and event-sourcing. But it **cannot be claimed churn-proof** because (a) two grounding claims about current state are false in the direction that hides work, (b) the provider-agnostic guarantee is contradicted by the only existing implementation, and (c) several FK-in-a-blob and missing-index decisions are exactly the patterns that force migrations.

## Must-fix list to make it churn-proof (ordered)

1. **A1 — Kill native `jsonb`/`HasDefaultValueSql` everywhere; mandate `HasConversion`+`ValueComparer`; add a CI SQLite migration-parity test.** (The provider-agnostic claim is currently false.)
2. **C1 — `SharedBanSettings.TrustedChannelsJson` → real `SharedBanTrustedChannels` join table** (FK-in-blob; blocks erasure cascade + integrity). Sweep for other `List<guid>` FK-blobs.
3. **E2 — Wire the `ITenantScoped` global query filter + Postgres RLS policies and ship a tenant-isolation test in this delivery.** Isolation is currently unwired (zero filters in `OnModelCreating`).
4. **F1 + D5 — Add `(BroadcasterId, SubjectUserId)` indexes on every snapshot-bearing append-only table**, or GDPR erasure is an unbounded scan and the O(1) claim is false for `[PII-scrub]`.
5. **F2 — Model multi-subject event PII** (`EventSubjectKeys`) or enforce one-subject-per-event; gift/raid events currently can't be fully shredded.
6. **D3 — Make `EventJournal (BroadcasterId, StreamPosition)` a UNIQUE constraint**, and specify the app-level per-tenant sequence mechanism (A2).
7. **B2 + B3 — Add `Channels.Status` (suspend/churn) and `Subscriptions.TrialEndsAt`/`GracePeriodEndsAt`.** The IAM `tenant:suspend` permission and Stripe dunning have nowhere to write today.
8. **B4 (ConfigSchemaVersion) — Add a schema-version int to every app-interpreted `[VC:JSON]` config blob**, mirroring the event side, so config shape changes upcast instead of migrate.
9. **A5 — Add explicit `*Normalized` columns for every case-insensitive unique lookup** (`Channels.Name`, `Users.Username`, `Commands.Name`).
10. **G3 — Account for the live `Storage`/`Record` tables** in the deltas (keep/fold/drop), and **C3/G1 — keep `Pronouns` as a lookup table** instead of enum-ifying it.

Fix 1–6 are the load-bearing churn-proofing; 7–10 are the "obviously wish we had" column/lookup set. With those, the spec earns SHIP-READY. Without fix 1 and 3 in particular, the headline promises (provider-agnostic, isolated, O(1)-erasure) are unproven in code.

Grounding paths: `c:\Projects\NoMercyLabs\nomnomzbot\server\src\NomNomzBot.Infrastructure\Persistence\Configurations\ChannelConfiguration.cs` (native jsonb), `...\Infrastructure\DependencyInjection.cs:69` (Npgsql-only), `...\Infrastructure\Persistence\AppDbContext.cs:81-87` (no query filter wired), `...\Domain\Common\ITenantScoped.cs` (`string BroadcasterId`), `...\Domain\Entities\{Channel,User,Pronoun,Service,Permission}.cs`.