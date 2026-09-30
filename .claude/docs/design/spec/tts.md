# TTS Subsystem — Interface Specification

**Status:** Implementable. Code from this directly.
**As-built status:** two capabilities are **open**: per-segment utterances (**open — S054**, §3.4 / §6 / §6.2) and moderation retraction (**open — S-RETRACT-c**, §2 / §3.4a). `TtsConfigUpdatedEvent`, `ITtsService.SynthesizeForChannelAsync`, `ITtsAudioStore` and the `TtsCacheEntry` read/write path are not built. The controller layout in §5, the config fields in §1/§4 and the request/outcome shapes in §3.4 are the as-built ones.
**Grounding:** LOCKED schema (`2026-06-16-database-schema.md` Domain P / Q.1 / R.1), design doc (`2026-06-16-tts-stream-admin-devmode.md` §TTS), stack doc (`2026-06-16-stack-and-dependencies.md`), decisions doc (`2026-06-16-decisions-resolved.md`).
**Conventions:** C# namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR; `Newtonsoft.Json` for app JSON; surrogate guid PKs via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter; deployment-profile adapters chosen by DI.

> **Note on the live code (extend, do not duplicate).** A working TTS subsystem already exists: `ITtsService` (`Application/Contracts/Tts/ITtsService.cs`), `ITtsConfigService` (`Application/Services/ITtsConfigService.cs`), `ITtsProvider` (`Domain/Interfaces/ITtsProvider.cs`), `TtsService` + `EdgeTtsProvider`/`AzureTtsProvider`/`ElevenLabsTtsProvider` (`Infrastructure/Services/Tts/`), `TtsConfigService` (`Infrastructure/Services/Application/`), `TtsController` + `TtsConfigController` (`Api/Controllers/V1/`). This spec **extends those exact types** to the locked schema and adds the missing capabilities (per-channel `TtsConfig` table, BYOK key vault, opt-out profanity censor, mod-approval queue, content-addressed `StorageRef` cache, per-viewer voice, usage ledger, client-edge dispatch). The controller surface is split across `TtsConfigController`, `TtsQueueController` and `TtsVoiceDefaultsAdminController` — see §5. Two duplicate `StatusResponseDto<T>` exist (`Api.Models` and `Application.DTOs`); use **`Api.Models.StatusResponseDto<T>`** in controllers (matches `BaseController.ResultResponse`).

> **Migration deltas this spec assumes (load-bearing).** The live entities are pre-lock shapes that must be widened to match the LOCKED schema before this surface is correct:
> - `ITenantScoped.BroadcasterId` widens `string` → `Guid` (schema §1.1 decision #1). Every TTS entity's `BroadcasterId` becomes `Guid`.
> - `UserTtsVoice.Id`, `TtsUsageRecord.Id`, `TtsCacheEntry.Id`: `int` → `Guid` (UUIDv7).
> - `UserTtsVoice.UserId`: add `Guid` FK→`Users.Id`; add `UserTwitchUserId string(50)` indexed attribute **[PII-hash]**.
> - `TtsCacheEntry`: add `StorageRef`, `StorageKind`, `SizeBytes` (nullable `AudioData`); becomes GLOBAL (no `BroadcasterId`).
> - **New table `TtsConfig`** replaces the JSON-blob config (`Configuration` row keyed `"tts:config"`) the live `TtsConfigService` reads/writes. The LOCKED schema is **gaining** the BYOK envelope columns to match gdpr-crypto's `CipherPayload`: `AzureApiKeyNonce`, `AzureKeyVersion`, `ElevenLabsApiKeyNonce`, `ElevenLabsKeyVersion` (alongside the existing `*Cipher` + `SubjectKeyId`).
> - **New table `TtsApprovalQueueEntry`** for the mod-approval queue (no live equivalent).
> - `TtsUsageRecord`: add `WasCensored`, `WasModApproved`, `StreamId`, `OccurredAt` and is **[APPEND-ONLY]** (`CreatedAt` only).
> - `TtsVoice`: add `Accent`, `Age`, `StylesJson`, `TagsJson`, `Description`, `PreviewUrl` (all nullable) — catalog-search + preview metadata.
> - `TtsConfig`: add `ViewerVoiceSelfServiceEnabled bool` (default true).

---

## 1. Entities (owned by this subsystem)

All from the LOCKED schema Domain P (TTS) + Q.1 (`CryptoKey`, referenced) + R.1 (`Pronouns`, referenced for username pronunciation). Fields below name the load-bearing columns; the schema is authoritative for the full column list, indexes, and converter flags.

### P.1 `TtsConfig` — per-channel TTS behavior (tenant-scoped, soft-delete)
| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK (UUIDv7). |
| `BroadcasterId` | `Guid` | FK→`Channels.Id`, **Unique** (one config row per channel). `ITenantScoped`. |
| `IsEnabled` | `bool` | Master TTS toggle. |
| `Mode` | `string(20)` | `client_edge`\|`byok`\|`self_host`. [VC:enum]. Selects the provider-adapter plane. New-channel default (binding): `client_edge`. |
| `DefaultProvider` | `string(20)` | `edge`\|`azure`\|`elevenlabs`. [VC:enum]. New-channel default (binding): `edge`. |
| `DefaultVoiceId` | `string(255)?` | →`TtsVoice.Id`. |
| `ProfanityCensorEnabled` | `bool` | **Opt-OUT** light swear filter. New-channel default (binding): `true`; the streamer may disable. |
| `ModApprovalRequired` | `bool` | When true, utterances enter the approval queue, not direct dispatch. New-channel default (binding): `false`. |
| `MinPermission` | `string(20)` | Lowest community standing allowed to trigger TTS: `everyone`\|`subscribers`\|`vip`\|`moderators`\|`broadcaster` (default `everyone`). Enforced by the dispatch gate (`standing_gate` reject). |
| `SkipBotMessages` | `bool` | Intended: do not read the bot's own chat lines aloud (default `true`). **Persisted and returned, but no server-side reader enforces it yet.** |
| `ReadUsernames` | `bool` | Intended: prefix each chat utterance with the author's name (default `true`). **Persisted and returned, but no server-side reader enforces it yet.** |
| `ViewerVoiceSelfServiceEnabled` | `bool` | When true, a viewer may pick their OWN voice via the self-service route + `!voice` chat command (the moderator can still override any viewer). New-channel default (binding): `true`; the streamer may lock it off. |
| `MinBitsToTts` | `int?` | Null = no bits gate. |
| `MaxCharacters` | `int` | Per-utterance cap (column default 500), **tier-scaled** (binding): a safety baseline on the Base tier, higher tiers get a higher cap. Not a single hardcoded value — resolved from the channel's billing tier. |
| `AzureApiKeyCipher` | `text?` | **[PII-shred]** BYOK Azure key — base64 `CipherPayload.CipherText` (gdpr-crypto §4.1), AEAD under `SubjectKeyId`. |
| `AzureApiKeyNonce` | `text?` | base64 `CipherPayload.Nonce` for the Azure cipher (96-bit per-call nonce). |
| `AzureKeyVersion` | `int?` | `CryptoKey` row version bound into the Azure cipher's AAD (`keyVersion`). |
| `AzureRegion` | `string(50)?` | |
| `ElevenLabsApiKeyCipher` | `text?` | **[PII-shred]** BYOK ElevenLabs key — base64 `CipherPayload.CipherText`, AEAD under `SubjectKeyId`. |
| `ElevenLabsApiKeyNonce` | `text?` | base64 `CipherPayload.Nonce` for the ElevenLabs cipher. |
| `ElevenLabsKeyVersion` | `int?` | `CryptoKey` row version bound into the ElevenLabs cipher's AAD (`keyVersion`). |
| `SubjectKeyId` | `Guid?` | FK→`CryptoKey.Id` — the DEK wrapping the BYOK keys; destroying it crypto-shreds them. |
| `CreatedAt`/`UpdatedAt`/`DeletedAt` | `timestamp` | |

> **BYOK cipher envelope (gdpr-crypto, do not duplicate).** Each BYOK key is stored as the gdpr-crypto `CipherPayload(CipherText, Nonce)` envelope split across `*Cipher`/`*Nonce` columns plus the `*KeyVersion`. Encrypt/decrypt **only** via the token vault — `IIntegrationTokenVault` / `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` (gdpr-crypto §3.4) under `SubjectKeyId` — with `CipherAad(TenantId=BroadcasterId, Provider=azure|elevenlabs, TokenType=api_key, KeyVersion)`. This subsystem **does not** define its own AES-GCM primitive; `IFieldCipher` is gdpr-crypto's, reached through the vault.

### P.2 `TtsVoice` — global voice catalog (GLOBAL, seed)
`Id string(255) PK` (external voice id, not PII); `Name string(100)`; `DisplayName string(255)`; `Locale string(10) Index`; `Gender string(10)`; `Provider string(50) Index` (`edge`\|`azure`\|`elevenlabs`); `IsDefault bool`; **NEW metadata (the ElevenLabs/Polly label model, all nullable):** `Accent string(50)? Index` (e.g. American, British); `Age string(20)?` (e.g. young, middle_aged, old); `StylesJson string? [VC:JSON]` (list of provider style/emotion names, e.g. Azure cheerful/angry); `TagsJson string? [VC:JSON]` (searchable use-case labels, e.g. narration, gaming); `Description string(1000)?`; `PreviewUrl string(2048)?` (provider sample-audio url; null for edge/self-synth). **No `BroadcasterId`.** These power catalogue search/filter and a preview-before-pick UX; provider metadata that the adapters currently discard (ElevenLabs `preview_url` + labels) is now captured. (Live `TtsVoice : BaseEntity` gains the metadata columns.)

### P.3 `UserTtsVoice` — per-viewer voice assignment (tenant-scoped)
`Id Guid PK`; `BroadcasterId Guid FK→Channels Index`; `UserId Guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `VoiceId string(255)` (→`TtsVoice.Id`). **Unique** `(BroadcasterId, UserId)`.

### P.4 `TtsUsageRecord` — per-utterance cost/quota ledger (tenant-scoped, **[APPEND-ONLY]**)
`Id Guid PK`; `BroadcasterId Guid FK→Channels Index`; `UserId Guid FK→Users Index`; `UserTwitchUserId string(50) Index` **[PII-hash]**; `Provider string(20)`; `VoiceId string(255)`; `CharacterCount int`; `WasCensored bool`; `WasModApproved bool?`; `StreamId Guid? FK→Streams Index`; `OccurredAt timestamp Index`; `CreatedAt` (no `UpdatedAt`/`DeletedAt`).

### P.5 `TtsCacheEntry` — content-addressed audio cache (GLOBAL)
`Id Guid PK`; `ContentHash string(64) Unique Index`; `AudioData blob?` (null when not inline); `StorageRef string(2048)?` (disk path / object-store key); `StorageKind string(20)` (`inline`\|`disk`\|`object_store` [VC:enum]); `SizeBytes int?`; `DurationMs int`; `Provider string(20)`; `VoiceId string(255)`. **Unique** `ContentHash`. **No `BroadcasterId`** (cache is cross-tenant; key is content hash).

### `TtsApprovalQueueEntry` — mod-approval queue (tenant-scoped, soft-delete) — schema **P.1a**
> Defined in the LOCKED schema as **P.1a** (sibling of `TtsConfig`); the columns below are field references — the schema doc is the source of truth. One row per pending utterance.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK (UUIDv7). |
| `BroadcasterId` | `Guid` | FK→`Channels.Id`, Index. `ITenantScoped`. |
| `RequestedByUserId` | `Guid` | FK→`Users.Id`, Index. The viewer who triggered TTS. |
| `RequestedByTwitchUserId` | `string(50)` | Index. **[PII-hash]**. |
| `RequestedByDisplayName` | `string(255)?` | **[PII-scrub]** snapshot for the moderator UI. |
| `OriginalText` | `text` | **[PII-scrub]** raw text. |
| `CensoredText` | `text?` | **[PII-scrub]** post-censor text actually spoken on approval. |
| `VoiceId` | `string(255)` | Resolved voice. |
| `Provider` | `string(20)` | Resolved provider. |
| `Status` | `string(20)` | `pending`\|`approved`\|`rejected`\|`expired`. [VC:enum]. Index. |
| `WasCensored` | `bool` | Whether the censor altered the text. |
| `ReviewedByUserId` | `Guid?` | FK→`Users.Id`. The moderator who acted. |
| `ReviewedAt` | `timestamp?` | |
| `SourceMessageId` | `string(255)?` | Originating chat message id (for context). |
| `StreamId` | `Guid?` | FK→`Streams.Id`, Index. |
| `ExpiresAt` | `timestamp` | Index. Auto-expire stale entries (default: queued + 10 min). |
| `CreatedAt`/`UpdatedAt`/`DeletedAt` | `timestamp` | |

**Index** `(BroadcasterId, Status, CreatedAt)`. Purpose: cautious-streamer pre-speak review gate; default-deny when `ModApprovalRequired` is on.

### Referenced (not owned): `Pronouns` (R.1), `CryptoKey` (Q.1), `Channels` (A.2), `Users` (A.1), `Streams`.

---

## 2. Domain events

All inherit `DomainEventBase` (the **abstract class** defined in platform-conventions §2.0, providing `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; events must NOT redeclare these). Events are **sealed classes**. Published via `IEventBus` (singleton). File: `Domain/Tts/Events/TtsEvents.cs`, namespace `NomNomzBot.Domain.Tts.Events`. The four events below are built with these shapes.

```csharp
namespace NomNomzBot.Domain.Tts.Events;

/// <summary>A TTS utterance was synthesized and dispatched (direct or post-approval).</summary>
public sealed class TtsUtteranceDispatchedEvent : DomainEventBase
{
    public required string Text { get; init; }
    public required string VoiceId { get; init; }
    public required string Provider { get; init; }   // edge | azure | elevenlabs
    public required int CharacterCount { get; init; }
    public required int DurationMs { get; init; }
    public required string RequestedByTwitchUserId { get; init; }
    public required string DispatchMode { get; init; } // client_edge | self_host
    public string? ContentHash { get; init; }          // null for client_edge (no server audio)
    public string? AudioUrl { get; init; }
    public string? ChannelEventId { get; init; }       // activity-feed row id, so Replay can correlate the utterance
}

/// <summary>A TTS utterance was held for moderator approval.</summary>
public sealed class TtsUtteranceQueuedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required string OriginalText { get; init; }
    public required bool WasCensored { get; init; }
    public required string RequestedByTwitchUserId { get; init; }
}

/// <summary>A moderator approved or rejected a queued utterance.</summary>
public sealed class TtsUtteranceReviewedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required Guid ReviewedByUserId { get; init; }
    public required string Decision { get; init; }   // approved | rejected
}

/// <summary>A TTS utterance was suppressed before synthesis (gate failed).</summary>
public sealed class TtsUtteranceRejectedEvent : DomainEventBase
{
    public required string Reason { get; init; }     // disabled | bits_gate | standing_gate | empty | too_long | empty_after_censor | no_voice | unknown_voice
    public required string RequestedByTwitchUserId { get; init; }
}
```

**Open — S-RETRACT-c (not built).** The retraction event below is the target contract; it does not exist in code yet, and neither do `ITtsDispatchService.RetractAsync` nor the TTS-side moderation handler (§3.4a). `TtsConfigUpdatedEvent` is also not built.

```csharp
/// <summary>OPEN — S-RETRACT-c. A dispatched utterance must be retracted: its source message was deleted, or its author was
/// timed out / banned. Consumed by the overlay/TTS clients to stop playback mid-sentence and drop anything still queued.</summary>
public sealed class TtsUtteranceRetractedEvent : DomainEventBase
{
    public required Guid UtteranceId { get; init; }
    public required string Reason { get; init; }     // message_deleted | user_timeout | user_ban | mod_retract
    public Guid? RetractedByUserId { get; init; }    // null when automated
}

/// <summary>NOT BUILT. Per-channel TTS configuration changed.</summary>
public sealed class TtsConfigUpdatedEvent : DomainEventBase
{
    public required bool IsEnabled { get; init; }
    public required string Mode { get; init; }
    public required bool ProfanityCensorEnabled { get; init; }
    public required bool ModApprovalRequired { get; init; }
}
```

---

## 3. Service interfaces

> All `BroadcasterId` parameters are `Guid` (post-widening). Behavior notes state the state change / events emitted / side effects.

### 3.1 `ITtsProvider` (Domain — KEEP as-is)
`Domain/Interfaces/ITtsProvider.cs`. Per-provider synthesis adapter. **Unchanged surface** (already implemented by `EdgeTtsProvider`/`AzureTtsProvider`/`ElevenLabsTtsProvider`). Add one property to let the resolver pick by `TtsConfig.DefaultProvider` without prefix-sniffing:

```csharp
public interface ITtsProvider
{
    /// <summary>Stable provider key: edge | azure | elevenlabs.</summary>
    string ProviderKey { get; }                                                  // NEW — replaces Guid/prefix sniffing in TtsService.ResolveProvider

    Task<TtsSynthesisResult> SynthesizeAsync(string text, string voiceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TtsVoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default);
}
// TtsSynthesisResult { byte[] AudioData; int DurationMs; string Provider; string VoiceId; string ContentHash } — unchanged
// TtsVoiceInfo       { string Id; Name; DisplayName; Locale; Gender; Provider }                                 — unchanged
```
*Behavior:* `SynthesizeAsync` calls the provider's edge/HTTP API; returns audio bytes + content hash; never throws on provider error (returns empty result) — matches live impl.

### 3.2 `IByokTtsProviderFactory` (Application — NEW)
`Application/Contracts/Tts/IByokTtsProviderFactory.cs`. BYOK keys are per-channel and encrypted, so Azure/ElevenLabs providers cannot be plain singletons keyed off global config — they must be built per request from the channel's decrypted key.

```csharp
namespace NomNomzBot.Application.Contracts.Tts;

public interface IByokTtsProviderFactory
{
    /// <summary>
    /// Builds the channel's effective TTS provider for <paramref name="mode"/>:
    /// - client_edge / edge  → the shared EdgeTtsProvider (no key)
    /// - byok azure          → an AzureTtsProvider bound to the channel's decrypted Azure key+region
    /// - byok elevenlabs     → an ElevenLabsTtsProvider bound to the channel's decrypted key
    /// - self_host           → the operator-configured provider from app config
    /// Returns Failure (NOT_FOUND/SERVICE_UNAVAILABLE) when the required BYOK key is absent/undecryptable.
    /// </summary>
    Task<Result<ITtsProvider>> CreateForChannelAsync(Guid broadcasterId, string provider, CancellationToken ct = default);
}
```
*Behavior:* reads `TtsConfig`, decrypts the BYOK key via `IIntegrationTokenVault` / `ISubjectKeyService.UnprotectAsync(SubjectKeyId, CipherPayload(*Cipher, *Nonce), CipherAad(BroadcasterId, provider, "api_key", *KeyVersion))` (gdpr-crypto §3.4 — `Failure("KEY_DESTROYED")` surfaces when the DEK was crypto-shredded), constructs the provider. No persistence. No events. Does not define its own cipher.

### 3.3 `ITtsService` (Application — EXTEND)
`Application/Contracts/Tts/ITtsService.cs`. Keep the existing two methods; add the BYOK-aware overload and a cache-aware synth used by the dispatch path.

```csharp
namespace NomNomzBot.Application.Contracts.Tts;

public interface ITtsService
{
    // EXISTING — keep
    Task<TtsResult> SynthesizeAsync(string text, string voiceId, CancellationToken ct = default);
    Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default);

    // NEW — channel-aware synth (resolves provider via IByokTtsProviderFactory + checks TtsCacheEntry first)
    Task<Result<TtsResult>> SynthesizeForChannelAsync(Guid broadcasterId, string text, string voiceId, CancellationToken ct = default);
}
// TtsResult(byte[] AudioData, int DurationMs, string VoiceId, string Provider) — unchanged
```
*Behavior:* `SynthesizeForChannelAsync` — compute `ContentHash`; on cache hit load bytes (`AudioData` inline or via `ITtsAudioStore` from `StorageRef`) and return; on miss build the channel provider, synthesize, persist a `TtsCacheEntry`, return. No domain events (the orchestrator emits those). `SynthesizeAsync`/`GetAvailableVoicesAsync` unchanged from live impl.

### 3.4 `ITtsDispatchService` (Application — NEW; the orchestrator)
`Application/Contracts/Tts/ITtsDispatchService.cs`. The end-to-end utterance pipeline: gate → censor → queue-or-speak → dispatch → ledger. This is the load-bearing service the chat/redemption/pipeline callers use.

```csharp
namespace NomNomzBot.Application.Contracts.Tts;

public interface ITtsDispatchService
{
    /// <summary>
    /// Full utterance flow for one TTS request (as built).
    /// 1. Loads the channel config; disabled → TtsUtteranceRejectedEvent(disabled), Result.Failure(FEATURE_DISABLED).
    /// 2. Gates, each a reject event + Result.Failure: MinBitsToTts (bits_gate, VALIDATION_FAILED); MinPermission floor against
    ///    request.CommunityStanding (standing_gate, FORBIDDEN; an unknown standing counts as the lowest); empty text (empty).
    /// 3. Character cap = the STRICTER of the channel's MaxCharacters and the tier limit "tts_max_characters"
    ///    (GetLimitAsync; -1 / self-host = no tier clamp). Over-cap → too_long, VALIDATION_FAILED.
    /// 4. Opt-out profanity censor (only when ProfanityCensorEnabled); empty-after-censor → empty_after_censor.
    /// 5. Voice resolution: request.VoiceIdOverride → the requester's UserTtsVoice → config.DefaultVoiceId (the platform default
    ///    when the channel follows it) → the first catalogue voice. None → no_voice; an override naming an unknown voice → unknown_voice.
    /// 6. ModApprovalRequired → inserts TtsApprovalQueueEntry(pending), emits TtsUtteranceQueuedEvent, returns Queued.
    ///    (There is no queue bypass: a reviewer's own request is queued too.)
    ///    Else → under a per-channel serializer (ITtsChannelSerializer, so utterances synthesize and reach the overlay strictly in
    ///    request order) synthesizes, appends the TtsUsageRecord, emits TtsUtteranceDispatchedEvent, returns Dispatched.
    /// The overlay push is done by the hub broadcaster (TtsSpeakBroadcastHandler, an IEventHandler of
    /// TtsUtteranceDispatchedEvent) as a `tts_speak` widget event {text, voice, user, durationMs, audioUrl} to the TTS surface.
    /// </summary>
    Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(TtsSpeakRequest request, CancellationToken ct = default);

    /// <summary>Moderator approves a queued entry: sets status=approved, synthesizes+dispatches the censored text (a synthesis failure
    /// leaves the entry pending for retry), appends TtsUsageRecord(WasModApproved=true), emits TtsUtteranceReviewedEvent(approved)
    /// + TtsUtteranceDispatchedEvent. NOT_FOUND if no pending entry.</summary>
    Task<Result> ApproveAsync(Guid broadcasterId, Guid queueEntryId, Guid reviewedByUserId, CancellationToken ct = default);

    /// <summary>Moderator rejects a queued entry: sets status=rejected, emits TtsUtteranceReviewedEvent(rejected). No synthesis, no ledger row.</summary>
    Task<Result> RejectAsync(Guid broadcasterId, Guid queueEntryId, Guid reviewedByUserId, CancellationToken ct = default);

    /// <summary>Lists pending approval-queue entries for the moderator UI, newest-first, paged.</summary>
    Task<Result<PagedList<TtsQueueEntryDto>>> GetPendingQueueAsync(Guid broadcasterId, int page, int pageSize, CancellationToken ct = default);
}

// records live in Application/Contracts/Tts (see §4) — AS BUILT: one utterance, one text.
public sealed record TtsSpeakRequest(
    Guid BroadcasterId,
    Guid RequestedByUserId,
    string RequestedByTwitchUserId,   // the SPEAKER: whose voice is looked up; empty/"bot" style callers name no viewer
    string RequestedByDisplayName,
    string Text,
    string? VoiceIdOverride,          // honored only when it names a real catalogue voice (else unknown_voice)
    int BitsAmount,
    string CommunityStanding,         // everyone|subscriber|vip|artist|moderator (resolved by caller)
    string? SourceMessageId,
    Guid? StreamId,
    string? ChannelEventId = null,    // activity-feed row id, threaded to the dispatched event for Replay correlation
    double? RatePercent = null,
    double? PitchPercent = null);

public enum TtsDispatchDisposition { Dispatched, Queued }
public sealed record TtsDispatchOutcome(
    TtsDispatchDisposition Disposition,
    string VoiceId,
    string Provider,
    int CharacterCount,
    int DurationMs,
    string? PlaybackUrl);
```

#### 3.4b Segments — **open — S054 (not built)**

The target contract replaces `Text` + `VoiceIdOverride` on `TtsSpeakRequest` with an ordered segment list, so one utterance can switch voices mid-sentence and reach the overlay as ONE `tts_speak` payload with an ordered segment array (`widgets-overlays.md` §7). None of the types below exist in code yet; the as-built request above is one text in one voice. `RequestSpeakAsync` would resolve each segment's voice by the §6.2 precedence and honour a `BypassQueue` flag only for a requester holding `tts:queue:review` (otherwise the flag is ignored and the queue rule applies).

```csharp
// OPEN — S054. Not built.
public enum TtsVoiceMode { ChannelDefault, TriggeringUser, Explicit }
public sealed record TtsSegment(string Text, TtsVoiceMode VoiceMode, string? VoiceId); // VoiceId required when VoiceMode=Explicit
// TtsSpeakRequest would carry: IReadOnlyList<TtsSegment> Segments, bool BypassQueue = false
```

#### 3.4a Moderation retraction — TTS must un-say what was deleted — **open — S-RETRACT-c (not built)**

> **Status: open — S-RETRACT-c.** Not built: `ITtsDispatchService.RetractAsync`, `TtsUtteranceRetractedEvent` (§2), `TtsModerationRetractionHandler` and the `IOverlayClient.TtsRetract` push do not exist. The generic overlay-retraction plumbing (`OverlayModerationRetractionHandler`, `IOverlayRetractionNotifier` in the Widgets module) exists; the TTS surface is not yet hooked to it. The contract below is what S-RETRACT-c builds.

A deleted message that is still being read aloud is worse than the message: the deletion is
invisible to anyone listening, and the payload lands anyway. **Any moderation action that removes
a message must silence its audio, and a timeout/ban must silence everything still pending from
that author.**

`TtsModerationRetractionHandler` (`Infrastructure/Tts/EventHandlers/`, to be built) subscribes to the
moderation events — message deleted, user timed out, user banned, and the network-nuke batch —
and calls `RetractAsync` (target signature: `Task<Result<int>> RetractAsync(Guid broadcasterId, string? sourceMessageId, Guid? authorUserId, string reason, CancellationToken ct = default)` — drops matching pending queue entries as rejected, dispatches `IOverlayClient.TtsRetract`, emits `TtsUtteranceRetractedEvent` per affected utterance, idempotent) with the matching `SourceMessageId` (already carried on `TtsSpeakRequest`)
or the author's user id.

Ordering: retraction is dispatched on the **same hub connection** as the original `TtsSpeak`, so a
retraction can never overtake the utterance it cancels. An utterance whose audio has already
finished is a no-op, reported as zero affected rather than an error.

Applies to every entry point equally — chat TTS, channel-point redemptions, pipeline `tts_speak`,
and approved queue entries.

### 3.5 `ITtsProfanityCensor` (Application — NEW)
`Application/Contracts/Tts/ITtsProfanityCensor.cs`. The opt-out light swear filter — deliberately thin (AutoMod upstream is the real filter; do not duplicate it).

```csharp
namespace NomNomzBot.Application.Contracts.Tts;

public interface ITtsProfanityCensor
{
    /// <summary>Masks mild profanity in <paramref name="text"/> using a built-in light word list.
    /// Pure function; no I/O, no persistence, no events. Returns the (possibly unchanged) text and whether anything was masked.</summary>
    TtsCensorResult Censor(string text);
}
public sealed record TtsCensorResult(string Text, bool WasCensored);
```

### 3.6 `ITtsConfigService` (Application — EXTEND; back onto the `TtsConfig` table)
`Application/Tts/Services/ITtsConfigService.cs`. The impl is re-targeted from the JSON-blob `Configuration` row to the `TtsConfig` table and `broadcasterId` is `Guid`. As built, the per-viewer methods key the viewer by their **platform user id string** (what the dispatch resolver reads), and BYOK keys are written through dedicated methods, not `UpdateConfigAsync`.

```csharp
namespace NomNomzBot.Application.Tts.Services;

public interface ITtsConfigService
{
    Task<Result<TtsConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result<TtsConfigDto>> UpdateConfigAsync(Guid broadcasterId, UpdateTtsConfigDto request, CancellationToken cancellationToken = default);
    Task<Result<TtsConfigDto>> GetDefaultConfigAsync(CancellationToken cancellationToken = default);                     // the new-channel defaults
    Task<Result<TtsConfigDto>> ResetConfigAsync(Guid broadcasterId, CancellationToken cancellationToken = default);       // settings columns only; keeps BYOK keys, default voice, lexicon, per-viewer voices
    Task<Result<TtsConfigDto>> SetByokKeyAsync(Guid broadcasterId, string provider, SetTtsByokKeyDto request, CancellationToken cancellationToken = default);
    Task<Result<TtsConfigDto>> ClearByokKeyAsync(Guid broadcasterId, string provider, CancellationToken cancellationToken = default);
    Task<Result<PagedList<TtsVoiceDto>>> SearchVoicesAsync(TtsVoiceQuery query, CancellationToken cancellationToken = default);
    Task<Result<TtsTestResultDto>> TestVoiceAsync(Guid broadcasterId, TtsTestRequestDto request, CancellationToken cancellationToken = default);

    // per-viewer voice assignment (UserTtsVoice), moderator surface
    Task<Result<UserTtsVoiceDto>> GetUserVoiceAsync(Guid broadcasterId, string userId, CancellationToken cancellationToken = default);
    Task<Result<UserTtsVoiceDto>> SetUserVoiceAsync(Guid broadcasterId, string userId, SetUserVoiceDto request, CancellationToken cancellationToken = default);
    Task<Result> ClearUserVoiceAsync(Guid broadcasterId, string userId, CancellationToken cancellationToken = default);
    Task<Result<TtsVoiceImportResultDto>> ImportUserVoiceAssignmentsAsync(Guid broadcasterId, IReadOnlyList<TtsVoiceAssignmentRowDto> rows, bool createMissing = false, CancellationToken cancellationToken = default);

    // viewer self-service (caller acts on their OWN identity; gated on ViewerVoiceSelfServiceEnabled + IsEnabled)
    Task<Result<UserTtsVoiceDto>> SetOwnVoiceAsync(Guid broadcasterId, string viewerUserId, SetUserVoiceDto request, CancellationToken cancellationToken = default);
    Task<Result<UserTtsVoiceDto?>> GetOwnVoiceAsync(Guid broadcasterId, string viewerUserId, CancellationToken cancellationToken = default);
    Task<Result> ClearOwnVoiceAsync(Guid broadcasterId, string viewerUserId, CancellationToken cancellationToken = default);
}
```
*Behavior:*
- `GetConfigAsync` — reads the `TtsConfig` row (or returns the binding new-channel defaults if none: `Mode=client_edge`, `DefaultProvider=edge`, `ProfanityCensorEnabled=true`, `ModApprovalRequired=false`, `MaxCharacters`=the channel tier's resolved cap — `min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))`, where the resolver's `-1` (unlimited / self-host) maps to `AbsoluteMaxCharacters`); BYOK ciphers never returned in DTO (only the `HasAzureByokKey`/`HasElevenLabsByokKey` booleans).
- **As-built note (MaxCharacters, BYOK):** `UpdateConfigAsync` accepts `MaxCharacters` under a static `[Range(1, 500)]` and does **not** clamp it to the tier at write time; the tier limit is applied at dispatch (§3.4 step 3). BYOK keys are written by `SetByokKeyAsync` / `ClearByokKeyAsync`, and `FollowPlatformDefaultVoice = true` clears the channel voice so it follows the platform default. The bullet below is the target write-time clamp and encryption contract.
- `UpdateConfigAsync` — upserts `TtsConfig`; a streamer-supplied `MaxCharacters` is **clamped to the channel tier's resolved cap** — `effectiveCap = min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))` (the binding tier-scaled rule: a safety baseline on Base, higher tiers a higher ceiling; resolver `-1`→`AbsoluteMaxCharacters`) — so it can never exceed the tier ceiling, and a supplied value over `effectiveCap` is rejected with `VALIDATION_FAILED` (not silently truncated); encrypts any supplied BYOK key via `IIntegrationTokenVault` / `ISubjectKeyService.ProtectAsync(SubjectKeyId, plaintextKey, CipherAad(BroadcasterId, provider, "api_key", keyVersion), resourceTable: "TtsConfig", resourceColumn: "{Azure|ElevenLabs}ApiKeyCipher")` (gdpr-crypto §3.4 — mints/`GetOrCreate`s the `SubjectKeyId` `CryptoKey` if absent), persisting the returned `CipherPayload` into the `*Cipher`/`*Nonce` columns and the bound `*KeyVersion`; `SaveChangesAsync` via `IUnitOfWork`; emits `TtsConfigUpdatedEvent`. Never defines a parallel cipher.
- `SearchVoicesAsync` — free-text `Q` matches (case-insensitive contains) `Name`/`DisplayName`/`Description`/`TagsJson`; `Locale`/`Gender`/`Provider`/`Accent` are equality filters; ordered `Provider`→`Locale`→`Name`; paged; an empty query returns the first page of the whole catalog.
- `TestVoiceAsync` — synthesizes a short sample through `ITtsService.SynthesizeForChannelAsync`; returns base64 audio; no ledger row, no events. (Live impl returns `ExternalServiceUnavailable` on failure → keep `SERVICE_UNAVAILABLE`.)
- `SetUserVoiceAsync` — upserts `UserTtsVoice` `(BroadcasterId,UserId)`; validates `VoiceId` exists in `TtsVoice`; persists; no event.
- `ClearUserVoiceAsync` — soft-deletes the `UserTtsVoice` row; `NOT_FOUND` if absent.
- `SetOwnVoiceAsync`/`ClearOwnVoiceAsync`/`GetOwnVoiceAsync` — FIRST check the channel `TtsConfig`: `Failure(FEATURE_DISABLED)` when `IsEnabled==false` OR `ViewerVoiceSelfServiceEnabled==false`; then upsert/clear/read the caller's own `UserTtsVoice (BroadcasterId, callerUserId)`, validating the voice exists (Set). No event.

### 3.7 `ITtsAudioStore` (Application — NEW; cache `StorageRef` adapter)
`Application/Contracts/Tts/ITtsAudioStore.cs`. Abstracts where cached audio bytes live, matching `TtsCacheEntry.StorageKind` (`inline`\|`disk`\|`object_store`). Profile adapter (see §7).

```csharp
namespace NomNomzBot.Application.Contracts.Tts;

public interface ITtsAudioStore
{
    /// <summary>Persists audio for a content hash. Returns the storage descriptor to write onto TtsCacheEntry
    /// (StorageKind + StorageRef; bytes inline only for the in-row adapter). No domain events.</summary>
    Task<TtsStoredAudio> SaveAsync(string contentHash, byte[] audio, CancellationToken ct = default);

    /// <summary>Loads audio for a cache entry by its storage descriptor. Returns null if the backing object is missing.</summary>
    Task<byte[]?> LoadAsync(string storageKind, string? storageRef, byte[]? inlineData, CancellationToken ct = default);
}
public sealed record TtsStoredAudio(string StorageKind, string? StorageRef, byte[]? InlineData, int SizeBytes);
```

---

## 4. DTOs / contracts

`Application/Tts/Dtos/` (plus `Application/Contracts/Tts/` for dispatch records). Existing records kept; widened/added as noted.

```csharp
// NEW — TtsCharacterLimits.cs — absolute safety ceiling (the hard upper bound no tier can exceed).
// The EFFECTIVE per-utterance cap is the channel tier's resolved tts_max_characters limit
// (IBillingTierService.GetLimitAsync); this constant is only the absolute ceiling used as the
// static upper bound on UpdateTtsConfigDto.MaxCharacters and as the clamp for an unlimited (-1) tier.
// NOT BUILT as a type: as-built, UpdateTtsConfigDto.MaxCharacters is [Range(1, 500)] and the dispatch gate applies
// min(config.MaxCharacters, tier limit); the 8000 ceiling below is the target.
public static class TtsCharacterLimits
{
    /// <summary>Hard per-utterance character ceiling no billing tier may exceed.</summary>
    public const int AbsoluteMaxCharacters = 8000;
}

// AS BUILT — TtsConfigDtos.cs
public sealed record TtsConfigDto(
    bool IsEnabled,
    string Mode,                    // client_edge|byok|self_host
    string DefaultProvider,         // edge|azure|elevenlabs
    string? DefaultVoiceId,         // the EFFECTIVE voice: the platform default when the channel follows it
    int MaxCharacters,
    string MinPermission,           // everyone|subscribers|vip|moderators|broadcaster
    bool SkipBotMessages,           // persisted; not yet enforced server-side (§1)
    bool ReadUsernames,             // persisted; not yet enforced server-side (§1)
    bool ProfanityCensorEnabled,    // opt-out censor
    bool ModApprovalRequired,
    int? MinBitsToTts,
    bool ViewerVoiceSelfServiceEnabled = true,  // viewers may self-pick their own voice
    bool HasAzureByokKey = false,       // presence only — ciphertext never leaves the server (renamed from HasAzureKey)
    bool HasElevenLabsByokKey = false,  // (renamed from HasElevenLabsKey)
    string? AzureRegion = null,
    bool FollowsPlatformDefaultVoice = false);  // true when the channel has no voice of its own

public sealed record UpdateTtsConfigDto
{
    public bool? IsEnabled { get; init; }
    [RegularExpression("^(client_edge|byok|self_host)$")] public string? Mode { get; init; }
    [RegularExpression("^(edge|azure|elevenlabs)$")]      public string? DefaultProvider { get; init; }
    [MaxLength(255)] public string? DefaultVoiceId { get; init; }
    public bool? FollowPlatformDefaultVoice { get; init; }   // true → clear the channel voice; it follows the platform default
    [Range(1, 500)] public int? MaxCharacters { get; init; } // static range as built; tier limit applied at dispatch
    [RegularExpression("^(everyone|subscribers|vip|moderators|broadcaster)$")] public string? MinPermission { get; init; }
    public bool? SkipBotMessages { get; init; }
    public bool? ReadUsernames { get; init; }
    public bool? ProfanityCensorEnabled { get; init; }
    public bool? ModApprovalRequired { get; init; }
    [Range(0, 1_000_000)] public int? MinBitsToTts { get; init; }
    public bool? ViewerVoiceSelfServiceEnabled { get; init; }
    // BYOK keys are NOT on this DTO: they are write-only through PUT/DELETE config/byok/{provider} (SetTtsByokKeyDto).
}

public sealed record SetTtsByokKeyDto
{
    [Required, MaxLength(512)] public string ApiKey { get; init; } = null!;   // write-only; encrypted server-side, never echoed
    [MaxLength(50)] public string? Region { get; init; }
}

public sealed record TtsOverlayDto(string OverlayUrl, DateTime? LastRanAt);

// EXISTING — TtsVoiceDtos.cs — TtsVoiceDto EXTENDED with the catalog metadata
public sealed record TtsVoiceDto(string Id, string Name, string DisplayName, string Locale, string Gender, string Provider, bool IsDefault, string? Accent, string? Age, IReadOnlyList<string> Styles, IReadOnlyList<string> Tags, string? Description, string? PreviewUrl);
public sealed record TtsVoiceQuery(string? Q, string? Locale, string? Gender, string? Provider, string? Accent, int Page = 1, int PageSize = 50);
public sealed record TtsTestRequestDto { [Required, MaxLength(500)] public required string Text { get; init; } [Required, MaxLength(255)] public required string VoiceId { get; init; } }
public sealed record TtsTestResultDto(string VoiceId, string Provider, int DurationMs, string AudioBase64);

// AS BUILT — UserTtsVoiceDtos (in TtsConfigDtos.cs). UserId is the platform user id string; the route carries it.
public sealed record UserTtsVoiceDto(string UserId, string VoiceId);
public sealed record SetUserVoiceDto { [Required, MaxLength(255)] public string VoiceId { get; init; } = null!; }

// AS BUILT — TtsQueueEntryDto (Application/Contracts/Tts/ITtsDispatchService.cs)
public sealed record TtsQueueEntryDto(
    Guid Id, string RequestedByTwitchUserId, string? RequestedByDisplayName,
    string OriginalText, string? CensoredText, string VoiceId, bool WasCensored,
    string Status, DateTime CreatedAt, DateTime ExpiresAt, string? SourceMessageId);
```

---

## 5. Controller endpoints

**Three controllers** (`Api/Controllers/V1/`), not one: `TtsConfigController` (config, voices, lexicon, overlay, playback, per-viewer voices), `TtsQueueController` (the approval queue) and `TtsVoiceDefaultsAdminController` (Plane-C platform default voice). The two tenant controllers share the base route `api/v{version:apiVersion}/channels/{channelId}/tts` (`{channelId}` is a string — a ULID on the wire, see platform-conventions §5 "Wire id format"), are `[ApiVersion("1.0")]`, `[Authorize]`, and return via `BaseController.ResultResponse(...)` → `Api.Models.StatusResponseDto<T>` / `PaginatedResponse<T>`.

**Role gate:** every tenant route is tenant-scoped. Management routes carry a Gate-2 floor; the three `/me/voice` routes carry NO action key — the caller acts on their OWN identity, gated only by Gate-1 + the channel's `ViewerVoiceSelfServiceEnabled` + `IsEnabled` toggles (the service returns `FEATURE_DISABLED` when either is off). Gate-1 = `[Authorize]` + tenant resolution (pure entry — entry ≠ permission, floors are Gate-2's). Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the floor named in the action-key column (403 FORBIDDEN when below). The keys are seeded global `ActionDefinitions` (`roles-permissions.md` §7.1); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.

### 5.1 `TtsConfigController`

| Route (relative to base) | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/config` | GET | — | `StatusResponseDto<TtsConfigDto>` | management · `tts:config:read` |
| `/config` | PUT | `UpdateTtsConfigDto` | `StatusResponseDto<TtsConfigDto>` | management · `tts:config:write` |
| `/config/defaults` | GET | — | `StatusResponseDto<TtsConfigDto>` (new-channel defaults) | management · `tts:config:read` |
| `/config/reset` | POST | — | `StatusResponseDto<TtsConfigDto>` (settings columns reset in place; BYOK keys, default voice, lexicon, per-viewer voices kept) | management · `tts:config:write` |
| `/config/byok/{provider}` | PUT | `SetTtsByokKeyDto` | `StatusResponseDto<TtsConfigDto>` | management · `tts:config:write` |
| `/config/byok/{provider}` | DELETE | — | `StatusResponseDto<TtsConfigDto>` | management · `tts:config:write` |
| `/voices` | GET | `?q=&locale=&gender=&provider=&accent=&page=&pageSize=` | `PaginatedResponse<TtsVoiceDto>` | management · `tts:voice:read` |
| `/test` | POST | `TtsTestRequestDto` | `StatusResponseDto<TtsTestResultDto>` | management · `tts:voice:test` (write-expensive rate tier) |
| `/overlay` | GET | — | `StatusResponseDto<TtsOverlayDto>` | management · `tts:config:read` |
| `/overlay/test` | POST | — | `StatusResponseDto<TtsDispatchOutcome>` (fixed test line through the full `RequestSpeakAsync` path; no `ChannelEvent`) | management · `tts:voice:test` (write-expensive tier) |
| `/playback/skip` · `/playback/clear` · `/playback/pause` · `/playback/resume` | POST | — | `StatusResponseDto<object>` (`{ action }`) — fire-and-forget `tts_queue_control` widget event to the `tts_caption` overlay group; the server cannot see the client-side queue | management · `tts:playback:control` |
| `/lexicon` | GET | — | `StatusResponseDto<IReadOnlyList<TtsLexiconEntryDto>>` | management · `tts:config:read` |
| `/lexicon` | POST | `UpsertTtsLexiconEntryDto` | `StatusResponseDto<TtsLexiconEntryDto>` | management · `tts:config:write` |
| `/lexicon/{entryId}` | PUT | `UpsertTtsLexiconEntryDto` | `StatusResponseDto<TtsLexiconEntryDto>` | management · `tts:config:write` |
| `/lexicon/{entryId}` | DELETE | — | `StatusResponseDto<object>` | management · `tts:config:write` |
| `/voices/assignments/import` | POST | `List<TtsVoiceAssignmentRowDto>` (`?createMissing=`) | `StatusResponseDto<TtsVoiceImportResultDto>` | management · `tts:config:write` (write-expensive tier) |
| `/users/{userId}/voice` | GET | — | `StatusResponseDto<UserTtsVoiceDto>` | management · `tts:voice:read` |
| `/users/{userId}/voice` | PUT | `SetUserVoiceDto` | `StatusResponseDto<UserTtsVoiceDto>` | management · `tts:uservoice:write` |
| `/users/{userId}/voice` | DELETE | — | `StatusResponseDto<object>` | management · `tts:uservoice:write` |
| `/me/voice` | GET | — | `StatusResponseDto<UserTtsVoiceDto>` | self · self-scoped (no action key; gated on ViewerVoiceSelfServiceEnabled + IsEnabled) |
| `/me/voice` | PUT | `SetUserVoiceDto` | `StatusResponseDto<UserTtsVoiceDto>` | self · self-scoped (no action key; same gates) |
| `/me/voice` | DELETE | — | `StatusResponseDto<object>` | self · self-scoped (no action key; same gates) |

### 5.2 `TtsQueueController` — base `…/channels/{channelId}/tts/queue`

| Route | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/` | GET | `?page=&pageSize=` | `PaginatedResponse<TtsQueueEntryDto>` | management · `tts:queue:review` |
| `/{entryId:guid}/approve` | POST | — | `StatusResponseDto<object>` | management · `tts:queue:review` (write-expensive tier: approval synthesizes) |
| `/{entryId:guid}/reject` | POST | — | `StatusResponseDto<object>` | management · `tts:queue:review` |

### 5.3 `TtsVoiceDefaultsAdminController` — `api/v{version:apiVersion}/admin/platform-defaults/tts-voice` (Plane-C)

`[PlatformPlane]`, `[Authorize(Policy = "platform:defaults:manage")]`, `admin` rate tier. One edit moves every channel that follows the platform default voice (a channel with no voice of its own).

| Route | Verb | Request DTO | Response DTO | Plane / floor |
|---|---|---|---|---|
| `/` | GET | — | `StatusResponseDto<TtsVoiceDefaultDto>` (default + channels following / with own voice) | platform · `platform:defaults:manage` |
| `/candidates` | GET | — | `StatusResponseDto<IReadOnlyList<TtsVoiceCandidateDto>>` | platform · `platform:defaults:manage` |
| `/blast-radius` | POST | `TtsVoiceDefaultChange` | `StatusResponseDto<PlatformDefaultBlastRadiusDto>` (counted channels affected) | platform · `platform:defaults:manage` |
| `/` | PUT | `SetTtsVoiceDefaultRequest` (`VoiceId`, `ConfirmedChannelsAffected`) | `StatusResponseDto<TtsVoiceDefaultDto>` | platform · `platform:defaults:manage` (security-sensitive rate tier) |

> `action key` mapping (`ActionDefinitions`): `tts:config:read`, `tts:config:write`, `tts:voice:read`, `tts:voice:test`, `tts:uservoice:write`, `tts:queue:review`, `tts:playback:control` — all seeded with a Moderator floor (`tts:config:read` / `tts:voice:read` default Moderator with a Vip floor). Seeds live in `ActionDefinitionSeeder`. The `TtsVoice` catalog is reference data — seeded by `TtsVoiceSeeder` AND periodically synced from live provider voice lists via `ITtsVoiceCatalogSync` (§7), not a per-channel endpoint.

---

## 6. Pipeline actions

One action: **`play_tts`** (the `PlayMusic`/`SendMessage` sibling), `Infrastructure/Tts/PipelineActions/PlayTtsAction.cs`, implementing the **live `ICommandAction`** (contract in `stream-admin.md` §6: `ActionType`, `Category`/`Description` as resource keys, `Fields`, `ResolvesOwnTemplates`, `ExecuteAsync(PipelineExecutionContext, ActionDefinition)`). It is auto-discovered by the `ICommandAction` scan; there is no config record — parameters are read from the step's `ActionDefinition`.

- **`ActionType`:** `play_tts`; **Category:** `pipeline.category.tts`; **`ResolvesOwnTemplates`:** `true` (the action resolves its own templated fields, so the engine does not resolve them twice).
- **Params (as built, `Fields`):**
  - `text` — text, **required**, templated (`{{user.name}} says`, `{{args}}`); empty after resolution → `ActionResult.Failure`.
  - `voice` — voice picker, optional, templated; resolved to `TtsSpeakRequest.VoiceIdOverride`. Honored only when it names a real catalogue voice, else the dispatch rejects `unknown_voice`.
  - `as` — text, optional, templated; **whose voice speaks the line.** Empty or `user` keeps the trigger's voice; `bot` or `channel` names no viewer, so the line reads in the channel default voice; anything else is a literal platform user id (a flow can read a line as a specific person).
- **Behavior:** resolves the templates, builds ONE `TtsSpeakRequest` (single `Text`) from `PipelineExecutionContext`: `BroadcasterId`, the speaker from `as`, the trigger's REAL bits (`user.bits` variable) and community standing (`user.role`, default `everyone`) so the channel's bits gate and `MinPermission` floor see the true caller, `SourceMessageId = ctx.MessageId`, and `ChannelEventId = ctx.ChannelEventId` (so Replay correlates the utterance). It calls `ITtsDispatchService.RequestSpeakAsync` once and returns `ActionResult.Success` with the spoken/queued text on `Dispatched`/`Queued`, or `ActionResult.Failure(reason)` when the dispatch gate rejects. It emits no events directly (the dispatch service owns them).
- **Open — S054 (segments, not built).** The target `PlayTtsActionConfig(IReadOnlyList<TtsSegment> Segments, bool BypassQueue)` lets one utterance carry several segments, each with a voice mode (`ChannelDefault` | `TriggeringUser` | `Explicit`, §3.4b) — for example an announcer segment in the channel voice followed by the viewer's message in their own voice — with the editor showing a segment list and a per-segment voice-mode picker; `BypassQueue` would skip the approval queue only for a triggering user holding `tts:queue:review`. Until S054 lands, the `as` param is the single-utterance way to choose whose voice speaks.

### 6.2 System TTS surface + voice resolution

> **Status.** The system TTS surface and the ordered client-side queue are built. The **ordered segment array per `TtsSpeak` payload and the per-segment three-mode precedence are open — S054**; as built, one `tts_speak` event carries one text in one voice (`{text, voice, user, durationMs, audioUrl}`).

The TTS audio plays on the **system TTS surface** (`widgets-overlays.md` §1.2): a channel-owned page provisioned with the channel (never installed from the gallery; disable-only), owned by the TTS page, added to OBS once. It keeps an **ordered utterance queue** — one `TtsSpeak` payload = one queue item; items play one at a time, each item's `Segments` back-to-back — and renders an optional caption (`showText`). Per segment it plays `AudioUrl` when present (`byok`/`self_host`) or synthesizes via the browser's `speechSynthesis` on `client_edge`; on `client_edge` the SDK **must** set `utter.voice` and `utter.lang` from the segment's `VoiceId` (match on `TtsVoice.Id`, fall back to `Locale`) — never the browser default voice.

**Voice resolution — as built (per utterance):** `VoiceIdOverride` (the `voice` param) → the speaker's `UserTtsVoice` for this channel → `TtsConfig.DefaultVoiceId` (the platform default voice while the channel follows it) → the first catalogue voice; an override that is not a catalogue voice is rejected (`unknown_voice`), never silently replaced.

**Voice resolution (server-side, per segment, binding) — open — S054:**
1. `VoiceMode=Explicit` → the segment's `VoiceId` (`VALIDATION_FAILED` if it is not a catalogue voice).
2. `VoiceMode=TriggeringUser` → the triggering user's `UserTtsVoice` for this channel; when unset → step 3.
3. `VoiceMode=ChannelDefault` (and every fall-through) → `TtsConfig.DefaultVoiceId`; when unset → the provider's seeded `IsDefault` voice.

Precedence is therefore **explicit segment voice → triggering user's voice → channel default**. Voice lookup (`!voice <query>`, `SearchVoicesAsync`, `VoiceId` validation) matches `TtsVoice.Id` **and** `Locale` **case-insensitively** (`en-us` = `en-US`; `en-US-AriaNeural` = `en-us-arianeural`), then `Name`/`DisplayName` fuzzy.

## 6.1 Built-in chat command — `!voice` (viewer self-service)

A code-defined built-in command family (NOT a pipeline action; a sibling of the reserved built-ins), named `voice`, that lets a viewer manage their OWN voice from chat:

- `!voice` → replies with the caller's current voice (or the channel default if unset).
- `!voice <query>` → fuzzy-matches the catalog (`SearchVoicesAsync` with `Q=query`) and sets the caller's own voice to the best match via `SetOwnVoiceAsync`; replies naming the match; on multiple matches replies with the top few to disambiguate; on none replies "no voice matched".
- `!voice preview <query|id>` → synthesizes a short sample of the matched voice via `TestVoiceAsync`/dispatch (respects config); no persistence.
- `!voice clear` → `ClearOwnVoiceAsync`, reply confirming reset to default.
- `!voices` → replies with how to browse (the dashboard picker) + a couple example matches.

**Gating:** requires `TtsConfig.IsEnabled` AND `ViewerVoiceSelfServiceEnabled`; respects the channel's TTS `MinPermission` floor; per-user cooldown. It is a normal (non-reserved) built-in — the channel may disable it. It reads/writes state **only** via `ITtsConfigService` (`SearchVoicesAsync`/`SetOwnVoiceAsync`/`GetOwnVoiceAsync`/`ClearOwnVoiceAsync`) — no direct DB.

---

## 7. DI registration

`Infrastructure/DependencyInjection.cs`. Existing TTS lines (kept, lifetimes shown) plus additions. Providers stay singletons; channel-bound BYOK providers are built per request by the factory.

```csharp
// EXISTING — keep
services.AddHttpClient("edge-tts");
services.AddSingleton<ITtsProvider, EdgeTtsProvider>();           // shared, keyless
services.AddHttpClient("azure-tts");
services.AddHttpClient("elevenlabs-tts");
services.AddSingleton<ITtsService, TtsService>();                 // EXTEND impl (add SynthesizeForChannelAsync)
services.AddScoped<ITtsConfigService, TtsConfigService>();        // RE-TARGET impl to TtsConfig table

// NEW
services.AddScoped<ITtsDispatchService, TtsDispatchService>();    // orchestrator (DbContext + IUnitOfWork → scoped)
services.AddScoped<IByokTtsProviderFactory, ByokTtsProviderFactory>();
services.AddSingleton<ITtsProfanityCensor, TtsProfanityCensor>(); // pure, stateless

// Profile-adapter: audio store (matches TtsCacheEntry.StorageKind + DeploymentProfile)
//   self_host_lite  → DiskTtsAudioStore     (StorageKind=disk;  TTS_CACHE_PATH)
//   saas / full     → ObjectStoreTtsAudioStore (StorageKind=object_store; object key)
//   fallback/dev    → InlineTtsAudioStore   (StorageKind=inline; bytes in AudioData)
services.AddSingleton<ITtsAudioStore>(sp =>
    sp.GetRequiredService<IDeploymentProfileService>().Current.DbProvider == DbProviderKind.Sqlite
        ? new DiskTtsAudioStore(/* path from config */)
        : new ObjectStoreTtsAudioStore(/* ... */));

// Pipeline action
services.AddScoped<ICommandAction, PlayTtsAction>();              // auto-registered into ICommandActionRegistry

// Voice catalog sync — pulls live provider voice lists into the TtsVoice catalog
services.AddScoped<ITtsVoiceCatalogSync, TtsVoiceCatalogSync>();
```

**Voice catalog sync.** `ITtsVoiceCatalogSync` / `TtsVoiceCatalogSync`, on startup/seed (and an operator-triggerable refresh), pulls each provider's live `GetVoicesAsync` (Azure + ElevenLabs when an operator/BYOK key is configured; Edge from the static seed) and UPSERTS them into `TtsVoice` by `(Id)`, capturing the rich metadata (accent/age/styles/tags/description/previewUrl) the provider exposes — replacing today's "10 hardcoded Edge voices, provider lists discarded".

**Deployment-profile adapter variants:**
- **TTS `Mode` / provider plane** (per-channel, from `TtsConfig.Mode`, resolved at request time by `IByokTtsProviderFactory`): `client_edge` → `EdgeTtsProvider` + `OverlayHub` dispatch via `IOverlayClient.TtsSpeak(TtsSpeakPayload)` (zero server cost; the system TTS surface (§6.2) synthesizes/renders edge-side from the payload, no audio bytes leave the server); `byok` → per-channel `AzureTtsProvider`/`ElevenLabsTtsProvider` from decrypted key; `self_host` → operator-config provider.
- **`ITtsAudioStore`** chosen by `DeploymentProfile` (disk vs object-store vs inline), aligned to `TtsCacheEntry.StorageKind`.
- **BYOK key crypto** goes through gdpr-crypto's vault — `IIntegrationTokenVault` / `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` (envelope `CipherPayload`+`keyVersion`, AAD `tenantId‖provider‖tokenType‖keyVersion`) — whose `IKeyVault` KEK adapter (`local_aes` vs `kms_envelope`) is already selected by `DeploymentProfile.TokenVault`. This subsystem only references the vault service + `CryptoKey`; it neither picks the adapter nor defines a parallel cipher.

---

## 8. Dependencies (stack-doc libs)

| Need | Library (party) | Note |
|---|---|---|
| Edge/Azure/ElevenLabs HTTP/WS | `System.Net.WebSockets.ClientWebSocket`, `System.Net.Http` (1st, in-box) | Edge uses in-box WS; Azure/ElevenLabs via `IHttpClientFactory`. |
| Helix-call resilience on provider HTTP | `Microsoft.Extensions.Http.Resilience` 10.7.0 (2nd) | Retry/breaker on BYOK provider clients (don't hand-roll). |
| Content hashing / cache key | `System.Security.Cryptography` (SHA-256) (1st, in-box) | `ContentHash` for `TtsCacheEntry`. |
| BYOK key encryption / crypto-shred | gdpr-crypto vault — `IIntegrationTokenVault` / `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` over `IFieldCipher` (gdpr-crypto §3.2/§3.4) + `CryptoKey` DEK | Stored as `CipherPayload(CipherText, Nonce)` + `keyVersion`; AAD = `tenantId‖provider‖tokenType‖keyVersion` (anti-transplant). KEK custody (`local_aes` lite / `kms_envelope` SaaS) is the vault's `IKeyVault` adapter — **this subsystem references the vault, does not pick the adapter or define a parallel cipher**. |
| App JSON (DTO/config serialization) | `Newtonsoft.Json` (per project convention) | App JSON uses `Newtonsoft.Json` (binding project convention); this also fixes the `[VC:JSON]` converter serializer. See §9 decision 1. |
| Persistence | EF Core 10 (2nd) + provider adapter (Npgsql 10.0.2 / `EFCore.Sqlite`) | `[VC:JSON]`/`[VC:enum]` via hand-rolled `ValueConverter`+`ValueComparer`; no `jsonb`. |
| Cache L1/L2 | `Microsoft.Extensions.Caching.Hybrid` 10.7.0 (2nd) | Optional hot in-proc cache in front of `TtsCacheEntry` lookups. |
| Real-time client-edge dispatch | `Microsoft.AspNetCore.SignalR` (2nd) via `OverlayHub` → `IOverlayClient.TtsSpeak(TtsSpeakPayload)` (widgets-overlays §7 wire surface) | `client_edge` audio rendered in the system TTS surface (§6.2); server pushes a `TtsSpeakPayload` (voiceId/text/standing — see widgets-overlays `IOverlayClient`), never audio bytes. |
| Events | in-box `IEventBus` (1st) | No MediatR. |
| Tier-scaled character cap | `IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters")` (monetization-billing §3.2) | Resolves the EFFECTIVE per-utterance cap from the channel's billing tier (`-1`=unlimited→`TtsCharacterLimits.AbsoluteMaxCharacters`); injected into `ITtsDispatchService` (gate) + `TtsConfigService` (clamp). No new dep. |

No new third-party dependency is introduced by this subsystem.

---

## 9. Decisions (resolved)

1. **App-JSON serializer.** App JSON (DTO/config serialization) uses `Newtonsoft.Json`, per the binding CONVENTIONS line. The stack doc's preference for `System.Text.Json` does not govern this subsystem; the project convention is authoritative. The `[VC:JSON]` `ValueConverter` serializer is therefore `Newtonsoft.Json`.
2. **`TtsApprovalQueueEntry`.** Lives in the LOCKED schema as Domain P **P.1a** (tenant-scoped, soft-delete). Reference the schema directly as the source of truth for its columns.
3. **New-channel TTS defaults (binding).** A freshly created channel's `TtsConfig` materializes with `Mode=client_edge`, `DefaultProvider=edge` (zero server cost / no BYOK key required out of the box), `ProfanityCensorEnabled=true` (opt-out — the streamer may disable), and `ModApprovalRequired=false` (direct dispatch, no queue). These are the binding seeded defaults `GetConfigAsync` returns when no row exists.
4. **`MaxCharacters` is tier-scaled (binding).** The per-utterance character cap is not a single hardcoded constant: it resolves from the channel's billing tier — a safety baseline on the Base tier, with higher tiers granted a higher ceiling. The EFFECTIVE cap is `min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))` (monetization-billing §3.2; resolver `-1`=unlimited/self-host → the absolute ceiling). `TtsCharacterLimits.AbsoluteMaxCharacters` (8000) is the hard ceiling no tier can exceed and the only static bound left on the surface (`UpdateTtsConfigDto.MaxCharacters` `[Range(1, AbsoluteMaxCharacters)]` — the old static `[Range(1,500)]` is removed). The dispatch gate (§3.4) rejects over-cap utterances with `VALIDATION_FAILED`; `UpdateConfigAsync` rejects an over-cap configured value with `VALIDATION_FAILED` (never silently truncates). **As-built:** `tts_max_characters` is a seeded tier limit (500 / 2000 / 8000) and the dispatch gate applies `min(config.MaxCharacters, tier limit)`; `UpdateTtsConfigDto.MaxCharacters` keeps a static `[Range(1, 500)]` and `TtsCharacterLimits` is not built. **Dependency:** `"tts_max_characters"` must be a `TierLimit.LimitKey` enum value (LOCKED schema N.2) with seeded per-tier `LimitValue` rows (monetization-billing §8 pattern — additional `LimitKey` values read through the existing entitlement resolver); see report blocker — that enum addition + changelog entry lives in the schema/billing specs, not here.
5. **Voice catalog is searchable + rich.** The catalog carries the ElevenLabs/Polly label model (locale/gender/accent/age/style/tags/description/preview) and is queried via `SearchVoicesAsync` (free-text + filters + paging), not returned whole — so it scales past a handful of voices and supports preview-before-pick. Provider metadata the adapters used to discard is now captured by the catalog sync.
6. **Viewers self-select their voice (binding, toggle-default-on).** With TTS enabled, a viewer may set their OWN voice via `PUT /me/voice` (self-scoped, caller identity) and the `!voice` chat command — Firebot's model (each viewer owns their voice; the channel default reads for everyone else). Gated by `TtsConfig.ViewerVoiceSelfServiceEnabled` (default true; the streamer may lock it). Moderators retain the `/user-voice` override for setting others.
7. **Catalog sync.** `ITtsVoiceCatalogSync` upserts live provider voice lists (Azure/ElevenLabs when keyed, Edge from seed) into `TtsVoice`, so the browsable catalog reflects real provider inventory instead of a static 10.

---

## 10. As built — pronunciation lexicon + bulk voice-assignment import (2026-07-19)

The per-channel **pronunciation lexicon** shipped as `TtsLexiconEntry` (tenant-scoped, soft-delete; unique
`(BroadcasterId, Phrase, MatchKind)` filtered on `DeletedAt IS NULL`): `Phrase` (≤100) is spoken as
`Replacement` (≤200) with `MatchKind` = `word` (whole-word via lookarounds, case-insensitive — default) |
`exact` (case-sensitive literal). `ITtsLexiconService` (auto-registered by convention) owns CRUD + the
dispatch hot path `ApplyAsync`: rules are cached per channel (`tts:lexicon:{broadcasterId}`, 10 min TTL,
evicted on every write) and applied in ONE non-recursive pass over the original utterance — matches are
collected against the input (phrases regex-escaped, 250 ms per-rule match timeout), overlaps resolved
earliest-start → longest → rule order, output assembled once; bounded at 200 rules. Enforcement sits in
`TtsDispatchService.DispatchAsync`, the shared leg both direct dispatch and post-approval flow through, so
usernames and message content are rewritten on every plane (client_edge push and server-side synthesis
alike) — this one generic mechanism subsumes the legacy bot's username-pronunciation and slang-expansion
features. REST: `GET/POST /channels/{channelId}/tts/lexicon` + `PUT/DELETE /tts/lexicon/{entryId}` on
`TtsConfigController`, reusing the config keys (`tts:config:read` / `tts:config:write`). The dashboard TTS
page gained a "Pronunciation" section (list with match-kind chip, add/edit dialog, confirm-delete; Editor
manage floor).

**Bulk voice-assignment import** (the legacy 105-row migration surface, backend-only — no dashboard UI by
design): `POST /channels/{channelId}/tts/voices/assignments/import` (`tts:config:write`) accepts a raw
array `[{twitchUserId, voiceId}]`, ≤500 rows. Each row upserts `UserTtsVoice`; a Twitch id with no
`UserIdentity` (`unknown_user`) or a voice absent from the catalogue (`unknown_voice`, provider-enumeration
fallback pre-sync) is returned in `skipped` with its reason — no User rows are ever created. Response:
`{imported, skipped:[{twitchUserId, reason}]}` (`TtsVoiceImportResultDto`).

---

_End of spec._
