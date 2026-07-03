# TTS Subsystem — Interface Specification

**Status:** Implementable. Code from this directly.
**Grounding:** LOCKED schema (`2026-06-16-database-schema.md` Domain P / Q.1 / R.1), design doc (`2026-06-16-tts-stream-admin-devmode.md` §TTS), stack doc (`2026-06-16-stack-and-dependencies.md`), decisions doc (`2026-06-16-decisions-pending-confirmation.md`).
**Conventions:** C# namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR; `Newtonsoft.Json` for app JSON; surrogate guid PKs via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter; deployment-profile adapters chosen by DI.

> **Note on the live code (extend, do not duplicate).** A working TTS subsystem already exists: `ITtsService` (`Application/Contracts/Tts/ITtsService.cs`), `ITtsConfigService` (`Application/Services/ITtsConfigService.cs`), `ITtsProvider` (`Domain/Interfaces/ITtsProvider.cs`), `TtsService` + `EdgeTtsProvider`/`AzureTtsProvider`/`ElevenLabsTtsProvider` (`Infrastructure/Services/Tts/`), `TtsConfigService` (`Infrastructure/Services/Application/`), `TtsController` + `TtsConfigController` (`Api/Controllers/V1/`). This spec **extends those exact types** to the locked schema and adds the missing capabilities (per-channel `TtsConfig` table, BYOK key vault, opt-out profanity censor, mod-approval queue, content-addressed `StorageRef` cache, per-viewer voice, usage ledger, client-edge dispatch). The two duplicate controllers (`TtsController` + `TtsConfigController`, identical routes) collapse into one — see §5. Two duplicate `StatusResponseDto<T>` exist (`Api.Models` and `Application.DTOs`); use **`Api.Models.StatusResponseDto<T>`** in controllers (matches `BaseController.ResultResponse`).

> **Migration deltas this spec assumes (load-bearing).** The live entities are pre-lock shapes that must be widened to match the LOCKED schema before this surface is correct:
> - `ITenantScoped.BroadcasterId` widens `string` → `Guid` (schema §1.1 decision #1). Every TTS entity's `BroadcasterId` becomes `Guid`.
> - `UserTtsVoice.Id`, `TtsUsageRecord.Id`, `TtsCacheEntry.Id`: `int` → `Guid` (UUIDv7).
> - `UserTtsVoice.UserId`: add `Guid` FK→`Users.Id`; add `UserTwitchUserId string(50)` indexed attribute **[PII-hash]**.
> - `TtsCacheEntry`: add `StorageRef`, `StorageKind`, `SizeBytes` (nullable `AudioData`); becomes GLOBAL (no `BroadcasterId`).
> - **New table `TtsConfig`** replaces the JSON-blob config (`Configuration` row keyed `"tts:config"`) the live `TtsConfigService` reads/writes. The LOCKED schema is **gaining** the BYOK envelope columns to match gdpr-crypto's `CipherPayload`: `AzureApiKeyNonce`, `AzureKeyVersion`, `ElevenLabsApiKeyNonce`, `ElevenLabsKeyVersion` (alongside the existing `*Cipher` + `SubjectKeyId`).
> - **New table `TtsApprovalQueueEntry`** for the mod-approval queue (no live equivalent).
> - `TtsUsageRecord`: add `WasCensored`, `WasModApproved`, `StreamId`, `OccurredAt` and is **[APPEND-ONLY]** (`CreatedAt` only).

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
| `MinBitsToTts` | `int?` | Null = no bits gate. |
| `MaxCharacters` | `int` | Per-utterance cap, **tier-scaled** (binding): a safety baseline on the Base tier, higher tiers get a higher cap. Not a single hardcoded value — resolved from the channel's billing tier. |
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
`Id string(255) PK` (external voice id, not PII); `Name string(100)`; `DisplayName string(255)`; `Locale string(10) Index`; `Gender string(10)`; `Provider string(50) Index` (`edge`\|`azure`\|`elevenlabs`); `IsDefault bool`. **No `BroadcasterId`.** (Live `TtsVoice : BaseEntity` already matches.)

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

All inherit `DomainEventBase` (the `abstract record` defined in platform-conventions §2.0, providing `Guid EventId`, `Guid BroadcasterId`, `DateTimeOffset OccurredAt`; events must NOT redeclare these). Published via `IEventBus` (singleton). Naming matches the existing `…Event` convention. New file: `Domain/Events/TtsEvents.cs`.

```csharp
namespace NomNomzBot.Domain.Events;

/// <summary>A TTS utterance was synthesized and dispatched (direct or post-approval).</summary>
public sealed record TtsUtteranceDispatchedEvent : DomainEventBase
{
    public required Guid RequestedByUserId { get; init; }
    public required string VoiceId { get; init; }
    public required string Provider { get; init; }   // edge | azure | elevenlabs
    public required int CharacterCount { get; init; }
    public required bool WasCensored { get; init; }
    public required bool ViaApprovalQueue { get; init; }
    public required string DispatchMode { get; init; } // client_edge | self_host
    public string? ContentHash { get; init; }          // null for client_edge (no server audio)
    public int DurationMs { get; init; }
}

/// <summary>A TTS utterance was held for moderator approval.</summary>
public sealed record TtsUtteranceQueuedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required string OriginalText { get; init; }
    public required bool WasCensored { get; init; }
}

/// <summary>A moderator approved or rejected a queued utterance.</summary>
public sealed record TtsUtteranceReviewedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required Guid ReviewedByUserId { get; init; }
    public required string Decision { get; init; }   // approved | rejected
}

/// <summary>A TTS utterance was suppressed before synthesis (gate failed).</summary>
public sealed record TtsUtteranceRejectedEvent : DomainEventBase
{
    public required Guid RequestedByUserId { get; init; }
    public required string Reason { get; init; }     // disabled | role_floor | bits_floor | too_long | empty_after_censor
}

/// <summary>Per-channel TTS configuration changed.</summary>
public sealed record TtsConfigUpdatedEvent : DomainEventBase
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
    /// Full utterance flow for one TTS request.
    /// 1. Loads TtsConfig; if disabled → TtsUtteranceRejectedEvent(reason=disabled), Result.Failure(FEATURE_DISABLED).
    /// 2. Enforces gates (role floor via permission resolver, MinBitsToTts, character cap) → reject event + Result.Failure(FORBIDDEN/VALIDATION_FAILED) on fail.
    ///    The character cap is the channel tier's RESOLVED value: effectiveCap = min(AbsoluteMaxCharacters, GetLimitAsync(broadcasterId,"tts_max_characters")),
    ///    treating GetLimitAsync's -1 (unlimited / self-host) as AbsoluteMaxCharacters. Over-cap → TtsUtteranceRejectedEvent(too_long) + Result.Failure(VALIDATION_FAILED). No static 500 cap.
    /// 3. Applies the opt-out profanity censor (only when ProfanityCensorEnabled); empty-after-censor → reject(empty_after_censor).
    /// 4. If ModApprovalRequired → inserts TtsApprovalQueueEntry(status=pending), emits TtsUtteranceQueuedEvent, returns Result.Success(Queued).
    ///    Else → synthesizes via ITtsService.SynthesizeForChannelAsync, dispatches (client_edge: OverlayHub → IOverlayClient.TtsSpeak(TtsSpeakPayload) — the widget renders audio edge-side, no bytes on the wire; self_host: audio store), appends TtsUsageRecord, emits TtsUtteranceDispatchedEvent, returns Result.Success(Dispatched).
    /// </summary>
    Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(TtsSpeakRequest request, CancellationToken ct = default);

    /// <summary>Moderator approves a queued entry: sets status=approved, synthesizes+dispatches the censored text,
    /// appends TtsUsageRecord(WasModApproved=true), emits TtsUtteranceReviewedEvent(approved) + TtsUtteranceDispatchedEvent(ViaApprovalQueue=true).
    /// Failure NOT_FOUND if no pending entry; ALREADY_EXISTS-style CONFLICT if already reviewed.</summary>
    Task<Result> ApproveAsync(Guid broadcasterId, Guid queueEntryId, Guid reviewedByUserId, CancellationToken ct = default);

    /// <summary>Moderator rejects a queued entry: sets status=rejected, emits TtsUtteranceReviewedEvent(rejected). No synthesis, no ledger row.</summary>
    Task<Result> RejectAsync(Guid broadcasterId, Guid queueEntryId, Guid reviewedByUserId, CancellationToken ct = default);

    /// <summary>Lists pending approval-queue entries for the moderator UI, newest-first, paged.</summary>
    Task<Result<PagedList<TtsQueueEntryDto>>> GetPendingQueueAsync(Guid broadcasterId, int page, int pageSize, CancellationToken ct = default);
}

// records live in Application/Contracts/Tts (see §4)
public sealed record TtsSpeakRequest(
    Guid BroadcasterId,
    Guid RequestedByUserId,
    string RequestedByTwitchUserId,
    string RequestedByDisplayName,
    string Text,
    string? VoiceIdOverride,
    int BitsAmount,
    string CommunityStanding,     // everyone|subscriber|vip|artist|moderator (resolved by caller)
    string? SourceMessageId,
    Guid? StreamId);

public enum TtsDispatchDisposition { Dispatched, Queued }
public sealed record TtsDispatchOutcome(TtsDispatchDisposition Disposition, Guid? QueueEntryId, string? ContentHash);
```

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
`Application/Services/ITtsConfigService.cs`. Keep the existing method names/signatures (controllers + live impl depend on them) but **re-target the impl from the JSON-blob `Configuration` row to the new `TtsConfig` table** and widen `broadcasterId` to `Guid`. Add per-viewer voice management.

```csharp
namespace NomNomzBot.Application.Services;

public interface ITtsConfigService
{
    // EXISTING surface — keep names; broadcasterId widens string → Guid
    Task<Result<TtsConfigDto>> GetConfigAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result<TtsConfigDto>> UpdateConfigAsync(Guid broadcasterId, UpdateTtsConfigDto request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<TtsVoiceDto>>> GetVoicesAsync(CancellationToken cancellationToken = default);
    Task<Result<TtsTestResultDto>> TestVoiceAsync(Guid broadcasterId, TtsTestRequestDto request, CancellationToken cancellationToken = default);

    // NEW — per-viewer voice assignment (UserTtsVoice)
    Task<Result<UserTtsVoiceDto>> SetUserVoiceAsync(Guid broadcasterId, SetUserVoiceDto request, CancellationToken cancellationToken = default);
    Task<Result> ClearUserVoiceAsync(Guid broadcasterId, Guid userId, CancellationToken cancellationToken = default);
    Task<Result<UserTtsVoiceDto?>> GetUserVoiceAsync(Guid broadcasterId, Guid userId, CancellationToken cancellationToken = default);
}
```
*Behavior:*
- `GetConfigAsync` — reads the `TtsConfig` row (or returns the binding new-channel defaults if none: `Mode=client_edge`, `DefaultProvider=edge`, `ProfanityCensorEnabled=true`, `ModApprovalRequired=false`, `MaxCharacters`=the channel tier's resolved cap — `min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))`, where the resolver's `-1` (unlimited / self-host) maps to `AbsoluteMaxCharacters`); BYOK ciphers never returned in DTO (only `HasAzureKey`/`HasElevenLabsKey` booleans).
- `UpdateConfigAsync` — upserts `TtsConfig`; a streamer-supplied `MaxCharacters` is **clamped to the channel tier's resolved cap** — `effectiveCap = min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))` (the binding tier-scaled rule: a safety baseline on Base, higher tiers a higher ceiling; resolver `-1`→`AbsoluteMaxCharacters`) — so it can never exceed the tier ceiling, and a supplied value over `effectiveCap` is rejected with `VALIDATION_FAILED` (not silently truncated); encrypts any supplied BYOK key via `IIntegrationTokenVault` / `ISubjectKeyService.ProtectAsync(SubjectKeyId, plaintextKey, CipherAad(BroadcasterId, provider, "api_key", keyVersion), resourceTable: "TtsConfig", resourceColumn: "{Azure|ElevenLabs}ApiKeyCipher")` (gdpr-crypto §3.4 — mints/`GetOrCreate`s the `SubjectKeyId` `CryptoKey` if absent), persisting the returned `CipherPayload` into the `*Cipher`/`*Nonce` columns and the bound `*KeyVersion`; `SaveChangesAsync` via `IUnitOfWork`; emits `TtsConfigUpdatedEvent`. Never defines a parallel cipher.
- `TestVoiceAsync` — synthesizes a short sample through `ITtsService.SynthesizeForChannelAsync`; returns base64 audio; no ledger row, no events. (Live impl returns `ExternalServiceUnavailable` on failure → keep `SERVICE_UNAVAILABLE`.)
- `SetUserVoiceAsync` — upserts `UserTtsVoice` `(BroadcasterId,UserId)`; validates `VoiceId` exists in `TtsVoice`; persists; no event.
- `ClearUserVoiceAsync` — soft-deletes the `UserTtsVoice` row; `NOT_FOUND` if absent.

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

`Application/DTOs/Tts/`. Existing records kept; widened/added as noted.

```csharp
// NEW — TtsCharacterLimits.cs — absolute safety ceiling (the hard upper bound no tier can exceed).
// The EFFECTIVE per-utterance cap is the channel tier's resolved tts_max_characters limit
// (IBillingTierService.GetLimitAsync); this constant is only the absolute ceiling used as the
// static upper bound on UpdateTtsConfigDto.MaxCharacters and as the clamp for an unlimited (-1) tier.
public static class TtsCharacterLimits
{
    /// <summary>Hard per-utterance character ceiling no billing tier may exceed.</summary>
    public const int AbsoluteMaxCharacters = 8000;
}

// EXISTING — TtsConfigDtos.cs — EXTENDED to the TtsConfig table shape
public sealed record TtsConfigDto(
    bool IsEnabled,
    string Mode,                    // client_edge|byok|self_host
    string DefaultProvider,         // edge|azure|elevenlabs
    string? DefaultVoiceId,
    bool ProfanityCensorEnabled,    // opt-out censor
    bool ModApprovalRequired,
    int? MinBitsToTts,
    int MaxCharacters,
    bool HasAzureKey,               // presence only — ciphertext never leaves the server
    string? AzureRegion,
    bool HasElevenLabsKey);

public sealed record UpdateTtsConfigDto
{
    public bool? IsEnabled { get; init; }
    [RegularExpression("^(client_edge|byok|self_host)$")] public string? Mode { get; init; }
    [RegularExpression("^(edge|azure|elevenlabs)$")]      public string? DefaultProvider { get; init; }
    [MaxLength(255)] public string? DefaultVoiceId { get; init; }
    public bool? ProfanityCensorEnabled { get; init; }
    public bool? ModApprovalRequired { get; init; }
    [Range(0, 100000)] public int? MinBitsToTts { get; init; }
    // No static [Range] cap — MaxCharacters is tier-scaled. The only static bound is the absolute
    // safety ceiling TtsCharacterLimits.AbsoluteMaxCharacters (the hard upper bound no tier exceeds);
    // the effective per-channel cap is the channel tier's resolved tts_max_characters limit, enforced
    // in UpdateConfigAsync against IBillingTierService.GetLimitAsync (clamp) — see §3.6, §9 decision 4.
    [Range(1, TtsCharacterLimits.AbsoluteMaxCharacters)] public int? MaxCharacters { get; init; }
    [MaxLength(4096)]  public string? AzureApiKey { get; init; }       // write-only; encrypted server-side, never echoed
    [MaxLength(50)]    public string? AzureRegion { get; init; }
    [MaxLength(4096)]  public string? ElevenLabsApiKey { get; init; }  // write-only
}

// EXISTING — TtsVoiceDtos.cs — unchanged
public sealed record TtsVoiceDto(string Id, string Name, string DisplayName, string Locale, string Gender, string Provider, bool IsDefault);
public sealed record TtsTestRequestDto { [Required, MaxLength(500)] public required string Text { get; init; } [Required, MaxLength(255)] public required string VoiceId { get; init; } }
public sealed record TtsTestResultDto(string VoiceId, string Provider, int DurationMs, string AudioBase64);

// NEW — UserTtsVoiceDtos.cs
public sealed record UserTtsVoiceDto(Guid UserId, string VoiceId);
public sealed record SetUserVoiceDto
{
    [Required] public required Guid UserId { get; init; }
    [Required, MaxLength(255)] public required string VoiceId { get; init; }
}

// NEW — TtsQueueDtos.cs
public sealed record TtsQueueEntryDto(
    Guid Id, Guid RequestedByUserId, string? RequestedByDisplayName,
    string OriginalText, string? CensoredText, string VoiceId, string Provider,
    string Status, bool WasCensored, DateTime CreatedAt);
```

---

## 5. Controller endpoints

**One controller — `TtsController`** (`Api/Controllers/V1/`). **Delete `TtsConfigController`** (duplicate route). Base route already established by the live code: `api/v{version:apiVersion}/channels/{channelId}/tts`. `[ApiVersion("1.0")]`, `[Authorize]`, `[Tags("TTS")]`. `channelId` resolves to `BroadcasterId Guid` via the tenant middleware. Responses via `BaseController.ResultResponse(...)` → `Api.Models.StatusResponseDto<T>` / `PaginatedResponse<T>`.

**Role gate:** every route is tenant-scoped (management plane). Gate 1 = `[Authorize]` + tenant resolution (entry; any management level ≥ Moderator). Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)` enforces the per-route floor named in the Gate-2 action-key column before the service call (403 FORBIDDEN when below). The keys are seeded global `ActionDefinitions` (schema Domain B); a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. No platform-admin (Plane C) endpoints are owned here.

| Route (relative to base) | Verb | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `/config` | GET | — | `StatusResponseDto<TtsConfigDto>` | management / Moderator · `tts:config:read` |
| `/config` | PUT | `UpdateTtsConfigDto` | `StatusResponseDto<TtsConfigDto>` | management / Editor · `tts:config:write` (BYOK keys + safety toggles are sensitive) |
| `/voices` | GET | — | `StatusResponseDto<IReadOnlyList<TtsVoiceDto>>` | management / Moderator · `tts:voice:read` |
| `/test` | POST | `TtsTestRequestDto` | `StatusResponseDto<TtsTestResultDto>` | management / Moderator · `tts:voice:test` |
| `/user-voice` | PUT | `SetUserVoiceDto` | `StatusResponseDto<UserTtsVoiceDto>` | management / Moderator · `tts:uservoice:write` |
| `/user-voice/{userId}` | DELETE | — | `StatusResponseDto<object>` | management / Moderator · `tts:uservoice:write` |
| `/user-voice/{userId}` | GET | — | `StatusResponseDto<UserTtsVoiceDto>` | management / Moderator · `tts:uservoice:write` |
| `/queue` | GET | `?page=&pageSize=` | `PaginatedResponse<TtsQueueEntryDto>` | management / Moderator · `tts:queue:review` |
| `/queue/{entryId}/approve` | POST | — | `StatusResponseDto<object>` | management / Moderator · `tts:queue:review` |
| `/queue/{entryId}/reject` | POST | — | `StatusResponseDto<object>` | management / Moderator · `tts:queue:review` |

> `action key` mapping (Domain B `ActionDefinitions`): `tts:config:read`(floor Moderator), `tts:config:write`(floor Editor), `tts:voice:read`(Moderator), `tts:voice:test`(Moderator), `tts:uservoice:write`(Moderator), `tts:queue:review`(Moderator). Each section-5 endpoint is gated by Gate 2 — `IActionAuthorizationService.AuthorizeActionAsync(userId, channelId, actionKey)` — against its action key (`/voices` GET → `tts:voice:read`). Register these seeds in `DataSeeder`.

No platform-admin (Plane C) endpoints are owned here. `TtsVoice` catalog seeding is reference data (DataSeeder), not an endpoint.

---

## 6. Pipeline actions

One action: **`PlayTts`** (the `PlayMusic`/`SendMessage` sibling). Files: `Application/DTOs/Tts/PlayTtsActionConfig.cs` (config DTO) + `Infrastructure/Pipeline/Actions/PlayTtsAction.cs` (impl of the **single canonical `ICommandAction`** — `string Type` + `Task<ActionResult> ExecuteAsync(ActionContext, CancellationToken)`, owned by `commands-pipelines.md` §3.13).

- **Type string:** `play_tts`
- **Category:** `tts`
- **Config DTO** (read from `ActionContext.Parameters` / `ActionDefinition.Get*`):
  ```csharp
  public sealed record PlayTtsActionConfig(
      string Text,            // template, resolved via ITemplateResolver (e.g. "{{args}}")
      string? VoiceId,        // null → resolve UserTtsVoice then TtsConfig.DefaultVoiceId
      bool BypassQueue);      // ignored unless caller has tts:queue:review; default false
  ```
- **Behavior:** resolves the text template, builds a `TtsSpeakRequest` from `ActionContext` (`BroadcasterId`, `TriggeredByUserId`, display name, community standing supplied by the engine), calls `ITtsDispatchService.RequestSpeakAsync`. Returns `ActionResult.Success` with the spoken/queued text on `Dispatched`/`Queued`, `ActionResult.Failure(reason)` when the dispatch gate rejects. Emits no events directly (the dispatch service owns events). Registered in `ICommandActionRegistry`.

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
```

**Deployment-profile adapter variants:**
- **TTS `Mode` / provider plane** (per-channel, from `TtsConfig.Mode`, resolved at request time by `IByokTtsProviderFactory`): `client_edge` → `EdgeTtsProvider` + `OverlayHub` dispatch via `IOverlayClient.TtsSpeak(TtsSpeakPayload)` (zero server cost; the OBS widget synthesizes/renders edge-side from the payload, no audio bytes leave the server); `byok` → per-channel `AzureTtsProvider`/`ElevenLabsTtsProvider` from decrypted key; `self_host` → operator-config provider.
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
| Real-time client-edge dispatch | `Microsoft.AspNetCore.SignalR` (2nd) via `OverlayHub` → `IOverlayClient.TtsSpeak(TtsSpeakPayload)` (widgets-overlays §7 wire surface) | `client_edge` audio rendered in the OBS widget; server pushes a `TtsSpeakPayload` (voiceId/text/standing — see widgets-overlays `IOverlayClient`), never audio bytes. |
| Events | in-box `IEventBus` (1st) | No MediatR. |
| Tier-scaled character cap | `IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters")` (monetization-billing §3.2) | Resolves the EFFECTIVE per-utterance cap from the channel's billing tier (`-1`=unlimited→`TtsCharacterLimits.AbsoluteMaxCharacters`); injected into `ITtsDispatchService` (gate) + `TtsConfigService` (clamp). No new dep. |

No new third-party dependency is introduced by this subsystem.

---

## 9. Decisions (resolved)

1. **App-JSON serializer.** App JSON (DTO/config serialization) uses `Newtonsoft.Json`, per the binding CONVENTIONS line. The stack doc's preference for `System.Text.Json` does not govern this subsystem; the project convention is authoritative. The `[VC:JSON]` `ValueConverter` serializer is therefore `Newtonsoft.Json`.
2. **`TtsApprovalQueueEntry`.** Lives in the LOCKED schema as Domain P **P.1a** (tenant-scoped, soft-delete). Reference the schema directly as the source of truth for its columns.
3. **New-channel TTS defaults (binding).** A freshly created channel's `TtsConfig` materializes with `Mode=client_edge`, `DefaultProvider=edge` (zero server cost / no BYOK key required out of the box), `ProfanityCensorEnabled=true` (opt-out — the streamer may disable), and `ModApprovalRequired=false` (direct dispatch, no queue). These are the binding seeded defaults `GetConfigAsync` returns when no row exists.
4. **`MaxCharacters` is tier-scaled (binding).** The per-utterance character cap is not a single hardcoded constant: it resolves from the channel's billing tier — a safety baseline on the Base tier, with higher tiers granted a higher ceiling. The EFFECTIVE cap is `min(TtsCharacterLimits.AbsoluteMaxCharacters, IBillingTierService.GetLimitAsync(broadcasterId,"tts_max_characters"))` (monetization-billing §3.2; resolver `-1`=unlimited/self-host → the absolute ceiling). `TtsCharacterLimits.AbsoluteMaxCharacters` (8000) is the hard ceiling no tier can exceed and the only static bound left on the surface (`UpdateTtsConfigDto.MaxCharacters` `[Range(1, AbsoluteMaxCharacters)]` — the old static `[Range(1,500)]` is removed). The dispatch gate (§3.4) rejects over-cap utterances with `VALIDATION_FAILED`; `UpdateConfigAsync` rejects an over-cap configured value with `VALIDATION_FAILED` (never silently truncates). **Dependency:** `"tts_max_characters"` must be a `TierLimit.LimitKey` enum value (LOCKED schema N.2) with seeded per-tier `LimitValue` rows (monetization-billing §8 pattern — additional `LimitKey` values read through the existing entitlement resolver); see report blocker — that enum addition + changelog entry lives in the schema/billing specs, not here.

---

_End of spec._
