# Interface Specification — `gdpr-crypto` Subsystem

**Status:** Implementable spec. Code from this directly.
**Area:** crypto-shred service · per-subject DEK lifecycle (`CryptoKey`) · key-vault adapter (local-AES / envelope-KMS) · surrogate-key anonymization map · erasure requests + audit · consent · AES-256-GCM + HKDF primitives.
**Grounding:** LOCKED schema (`2026-06-16-database-schema.md` Domains O + Q, §5 GDPR story) · `2026-06-16-gdpr-and-data.md` · `2026-06-16-stack-and-dependencies.md` (Crypto/secrets decision) · `2026-06-16-decisions-pending-confirmation.md` (#10: O(1) ciphertext crypto-shred plus row-scrub, both in scope and specified here).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork` (no raw `DbContext` in controllers); typed-interface DI, NO MediatR/Roslyn; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`; Newtonsoft.Json for app JSON; surrogate PK = `Guid` via `Guid.CreateVersion7()`; tenant key `BroadcasterId` is `Guid`; soft-delete (`IsDeleted`+`DeletedAt`) global filter.

> **Extends, does not duplicate.** The live `IEncryptionService` (`NomNomzBot.Application.Common.Interfaces`, AES-CBC, key=`SHA256(rawKey)`, no MAC, no AAD — cross-tenant transplant defect) and live `IGdprService` (`NomNomzBot.Application.Services`, string-id, no DEK/audit) are **superseded** by the interfaces below. The live `DeletionAuditLog` (`int` PK) is replaced by `ComplianceAuditLog` (O.10). `IEncryptionService` is **retained for backward-compat read** during token re-encryption migration, then retired. New tenant-scoped entities widen to `BroadcasterId : Guid` per the locked `ITenantScoped` widening (schema §1.1) — this subsystem MUST be written against `Guid`, not the current `string` `ITenantScoped`.

---

## 1. Entities

All owned by this subsystem (locked schema — field detail lives there, referenced by section, **not redefined**). Surrogate PK `Id : Guid` (UUIDv7) unless noted `bigint` (append-only). Every column's PII class (`[PII-hash]`/`[PII-scrub]`/`[PII-shred]`/`[PII-S9]`) is per schema §1.5.

| Entity | Schema § | Key fields (type) | Role in this subsystem |
|---|---|---|---|
| `CryptoKey` | Q.1 | `Id Guid` PK · `KeyScope string(20)` (`tenant`\|`subject`\|`platform`) · `BroadcasterId Guid?` · `SubjectIdHash string(64)?` · `WrappedKeyMaterial text?` (DEK ciphertext wrapped by KEK; never plaintext) · `KekReference string(255)?` · `Provider string(20)` (`kms_envelope`\|`local_aes`) · `Algorithm string(30)` (`AES-256-GCM`) · `Status string(20)` (`active`\|`rotating`\|`destroyed`) · `DestroyedAt timestamp?` · `ErasureRequestId Guid?` (FK→`ErasureRequest`) · `RotatedFromKeyId Guid?` (FK self) | The DEK registry. Destroy a row = crypto-shred O(1). FK target of `Users.SubjectKeyId`, `IamPrincipals.SubjectKeyId`, `IntegrationTokens.EncryptionKeyId`, `EventJournal.SubjectKeyId`, `EventSubjectKeys.SubjectKeyId`, `EventSnapshot.SubjectKeyId`, `ConsentRecords.SubjectKeyId`, `Subscriptions.SubjectKeyId`, `TtsConfig.SubjectKeyId`. |
| `KeyUsageBinding` | Q.2 | `Id bigint` PK · `CryptoKeyId Guid` (FK→`CryptoKey`) · `ResourceTable string(100)` · `ResourceColumn string(100)` · `BroadcasterId Guid?` · **UNIQUE** `(CryptoKeyId, ResourceTable, ResourceColumn)` | Inventory of which table/column is encrypted under each DEK. Feeds `ComplianceAuditLog.KeysShredded` and shred-impact reporting. |
| `EventSubjectKeys` | O.1a | `Id Guid` PK · `EventId Guid` (FK→`EventJournal.EventId`, Unique target) · `BroadcasterId Guid?` · `SubjectIdHash string(64)` · `SubjectKeyId Guid` (FK→`CryptoKey`) · `Role string(20)?` (`gifter`\|`recipient`\|`raider`\|`raided`) · **UNIQUE** `(EventId, SubjectKeyId)` | Per-subject DEK link for multi-subject events (gift sub / raid) so erasing one subject shreds only their payload slice. |
| `ConsentRecords` | O.5 | `Id Guid` PK · `BroadcasterId Guid?` (null = platform-wide ToS) · `SubjectUserId Guid` (FK→`Users`) · `SubjectKeyId Guid` (FK→`CryptoKey`) · `SubjectIdHash string(64)` · `ConsentType string(50)` · `Status string(20)` (`granted`\|`withdrawn`\|`expired`) · `LawfulBasis string(30)` (`consent`\|`contract`\|`legitimate_interest`) · `ConsentVersion string(20)?` · `Source string(50)?` · `IpAddressCipher string(255)?` **[PII-shred]** · `GrantedAt` · `WithdrawnAt?` · `ExpiresAt?` · **UNIQUE** `(BroadcasterId, SubjectUserId, ConsentType)` | Authoritative consent / lawful-basis ledger. `ConsentType` ∈ `tos_privacy`\|`age_18_gambling`\|`pronoun_special_category`\|`leaderboard_opt_in`\|`marketing`. |
| `ErasureRequest` | O.6 | `Id Guid` PK · `SubjectUserId Guid` (FK→`Users`) · `SubjectKeyId Guid` (FK→`CryptoKey`) · `SubjectIdHash string(64)` · `BroadcasterId Guid?` · `RequestType string(20)` (`erasure`\|`export`\|`opt_out`) · `RequestedBy string(20)` (`self_service`\|`broadcaster`\|`platform_iam`) · `Status string(20)` (`pending`\|`running`\|`completed`\|`failed`\|`cancelled`) · `Scope string(20)` (`deployment`\|`instance`\|`channel`) · `CryptoShredApplied bool` · `AnonymizationApplied bool` · `ExportLocation string(2048)?` · `ExportFormat string(20)?` · `RowsAffected int` · `FailureReason text?` · `RequestedAt` · `CompletedAt?` | Lifecycle of erasure/export/opt-out requests; drives the self-service my-data page. |
| `ComplianceAuditLog` | O.10 | `Id bigint` PK **[APPEND-ONLY]** · `RequestType string(20)` (`erasure`\|`export`\|`consent_change`) · `ErasureRequestId Guid?` (FK) · `SubjectIdHash string(64)` · `BroadcasterId Guid?` · `RequestedBy string(20)` (`self_service`\|`broadcaster`\|`platform_iam`\|`system`) · `TablesAffected text` **[VC:JSON]** `List<string>` · `RowsAffected int` · `KeysShredded int` · `Outcome string(20)` (`completed`\|`partial`\|`failed`) · `CompletedAt` | Append-only audit for erasure/export/consent. Retains only the **hashed** subject id, never reversible PII. Supersedes `DeletionAuditLog`. |

**Shared by subsystem, not owned:** `Users.SubjectKeyId` / `Users.IsAnonymized` / `Users.TwitchUserId` (A.1), `Users.PronounId` (A.1, [PII-S9]), `IntegrationTokens.EncryptionKeyId`/`CipherText`/`Nonce` (E.2), `EventJournal.SubjectKeyId`/`Payload`/`PayloadIsEncrypted` (O.1), `EventSnapshot.SubjectKeyId` (O.2), `Subscriptions.SubjectKeyId` (Domain N), `TtsConfig.SubjectKeyId` (P.1), `AuthSessions.IpAddressCipher` (A.3), `IamAuditLog.SourceIpCipher` (O.9), `AppSetting.SecureValueCipher` (P.11). This subsystem reads/writes those columns through the services below; the owning subsystems define the rows.

---

## 2. Domain events

Published via existing `NomNomzBot.Domain.Interfaces.IEventBus` (no MediatR). All are immutable `record`s under `NomNomzBot.Domain.Events.Gdpr`. They drive `ComplianceAuditLog` projections, SignalR my-data-page updates, and downstream cache/session invalidation. Payloads carry only the **hashed** subject id (never raw Twitch id / username).

```csharp
namespace NomNomzBot.Domain.Events.Gdpr;

public sealed record DataEncryptionKeyCreated(
    Guid CryptoKeyId, string KeyScope, Guid? BroadcasterId, string? SubjectIdHash,
    string Provider, string Algorithm, DateTime OccurredAt);

public sealed record DataEncryptionKeyRotated(
    Guid NewCryptoKeyId, Guid RotatedFromKeyId, Guid? BroadcasterId, string? SubjectIdHash,
    int ReEncryptedRowCount, DateTime OccurredAt);

public sealed record DataEncryptionKeyDestroyed(
    Guid CryptoKeyId, string KeyScope, Guid? BroadcasterId, string? SubjectIdHash,
    Guid ErasureRequestId, int KeysShredded, DateTime OccurredAt);

public sealed record SubjectErasureRequested(
    Guid ErasureRequestId, Guid SubjectUserId, string SubjectIdHash, Guid? BroadcasterId,
    string RequestType, string RequestedBy, string Scope, DateTime OccurredAt);

public sealed record SubjectErasureCompleted(
    Guid ErasureRequestId, Guid SubjectUserId, string SubjectIdHash, Guid? BroadcasterId,
    bool CryptoShredApplied, bool AnonymizationApplied, int KeysShredded, int RowsAffected,
    DateTime OccurredAt);

public sealed record SubjectErasureFailed(
    Guid ErasureRequestId, Guid SubjectUserId, string SubjectIdHash, string FailureReason,
    DateTime OccurredAt);

public sealed record SubjectDataExported(
    Guid ErasureRequestId, Guid SubjectUserId, string SubjectIdHash, Guid? BroadcasterId,
    string ExportFormat, string ExportLocation, int RowsAffected, DateTime OccurredAt);

public sealed record ConsentChanged(
    Guid ConsentRecordId, Guid SubjectUserId, string SubjectIdHash, Guid? BroadcasterId,
    string ConsentType, string Status, string LawfulBasis, string? ConsentVersion, DateTime OccurredAt);
```

---

## 3. Service interface(s)

Layer placement: interfaces in `NomNomzBot.Application/Common/Interfaces/Crypto/` (vault primitives) and `NomNomzBot.Application/Services/` (use-case services, matching existing `IGdprService` location). Impls in `NomNomzBot.Infrastructure/Services/Security/` (crypto) and `NomNomzBot.Infrastructure/Services/Application/` (use cases). All async, `CancellationToken cancellationToken = default` last, `Result`/`Result<T>` returns. Persistence via `IApplicationDbContext` + `IUnitOfWork` (transaction boundary), never raw `DbContext` in callers above Infrastructure.

### 3.1 `IKeyVault` — KEK custody adapter (profile-selected)

`NomNomzBot.Application/Common/Interfaces/Crypto/IKeyVault.cs`. Wraps/unwraps the 32-byte DEK with the deployment's KEK. Two impls (§7). Operates on raw key bytes only; knows nothing of EF/rows.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Crypto;

public interface IKeyVault
{
    string Provider { get; } // "local_aes" | "kms_envelope" — written to CryptoKey.Provider.

    // Wraps a freshly generated 32-byte DEK under the active KEK. Returns wrapped ciphertext +
    // the KEK reference to persist in CryptoKey.WrappedKeyMaterial/KekReference. No DB write.
    Task<Result<WrappedKey>> WrapAsync(ReadOnlyMemory<byte> dataEncryptionKey, CancellationToken cancellationToken = default);

    // Unwraps WrappedKeyMaterial back to the 32-byte DEK for in-process AEAD. The returned buffer
    // is caller-zeroed via CryptographicOperations.ZeroMemory after use. No DB write.
    Task<Result<byte[]>> UnwrapAsync(string wrappedKeyMaterial, string? kekReference, CancellationToken cancellationToken = default);
}
```

### 3.2 `IFieldCipher` — AEAD data-plane primitive (replaces `IEncryptionService`)

`NomNomzBot.Application/Common/Interfaces/Crypto/IFieldCipher.cs`. AES-256-GCM, `tagSizeInBytes:16`, 96-bit random nonce per call, **AAD = `tenantId‖provider‖tokenType‖keyVersion`** (anti-transplant per stack doc). Stateless; takes the unwrapped DEK explicitly. No DB.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Crypto;

public interface IFieldCipher
{
    // Encrypts plaintext under the supplied DEK. AAD binds the ciphertext to its row context so a
    // blob cannot be transplanted to another tenant/provider/token-type/key-version. Returns base64
    // ciphertext + base64 nonce to persist (e.g. IntegrationTokens.CipherText/Nonce).
    Result<CipherPayload> Encrypt(ReadOnlySpan<byte> dataEncryptionKey, string plaintext, CipherAad aad);

    // Decrypts; fails closed (Result.Failure "DECRYPT_FAILED") on tag mismatch / AAD mismatch /
    // tampering — never throws to the caller.
    Result<string> Decrypt(ReadOnlySpan<byte> dataEncryptionKey, CipherPayload payload, CipherAad aad);
}
```

### 3.3 `IKdf` — HKDF subkey derivation primitive

`NomNomzBot.Application/Common/Interfaces/Crypto/IKdf.cs`. HKDF-SHA256 for purpose-separated subkeys from a master/DEK (e.g. per-column separation). Stateless; in-box `System.Security.Cryptography.HKDF`. No DB.

```csharp
namespace NomNomzBot.Application.Common.Interfaces.Crypto;

public interface IKdf
{
    // Derives a length-byte subkey from inputKeyMaterial bound to a purpose label (info) + salt.
    // Pure function over in-box HKDF.DeriveKey; no allocation of secrets beyond the return buffer.
    Result<byte[]> DeriveKey(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt, string purpose, int length);
}
```

### 3.4 `ISubjectKeyService` — per-subject/tenant DEK lifecycle (`CryptoKey`)

`NomNomzBot.Application/Services/ISubjectKeyService.cs`. Owns `CryptoKey` + `KeyUsageBinding` rows. Composes `IKeyVault` + `IFieldCipher`. The only component that materializes plaintext DEKs (transiently, zeroed after use).

```csharp
namespace NomNomzBot.Application.Services;

public interface ISubjectKeyService
{
    // Generates a 32-byte DEK (RandomNumberGenerator), wraps via IKeyVault, persists an `active`
    // CryptoKey (KeyScope/BroadcasterId/SubjectIdHash set), saves via IUnitOfWork, emits
    // DataEncryptionKeyCreated. Idempotent per (KeyScope, BroadcasterId, SubjectIdHash) — returns the
    // existing active key if one is present.
    Task<Result<Guid>> GetOrCreateSubjectKeyAsync(Guid subjectUserId, string subjectIdHash, CancellationToken cancellationToken = default);
    Task<Result<Guid>> GetOrCreateTenantKeyAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result<Guid>> GetOrCreatePlatformKeyAsync(string purpose, CancellationToken cancellationToken = default);

    // Encrypts plaintext under the named DEK: loads CryptoKey, unwraps via vault, AEAD-encrypts via
    // IFieldCipher with the supplied AAD, returns CipherPayload to persist. Records/asserts a
    // KeyUsageBinding for (cryptoKeyId, resourceTable, resourceColumn). Fails CLOSED if Status != active.
    Task<Result<CipherPayload>> ProtectAsync(Guid cryptoKeyId, string plaintext, CipherAad aad, string resourceTable, string resourceColumn, CancellationToken cancellationToken = default);

    // Decrypts a CipherPayload under the named DEK. Returns Failure "KEY_DESTROYED" when the DEK was
    // crypto-shredded (Status=destroyed) — the GDPR guarantee surfaced to callers, not an exception.
    Task<Result<string>> UnprotectAsync(Guid cryptoKeyId, CipherPayload payload, CipherAad aad, CancellationToken cancellationToken = default);

    // Rotates a DEK: generates a successor (RotatedFromKeyId set), re-encrypts every row inventoried
    // in KeyUsageBinding under one IUnitOfWork transaction, flips old Status active→rotating→(retained),
    // emits DataEncryptionKeyRotated. Old key NOT destroyed (rotation ≠ shred).
    Task<Result<Guid>> RotateKeyAsync(Guid cryptoKeyId, CancellationToken cancellationToken = default);

    // CRYPTO-SHRED (O(1)). Sets Status=destroyed, nulls WrappedKeyMaterial, sets DestroyedAt +
    // ErasureRequestId, in one transaction. All ciphertext under the DEK becomes permanently
    // unreadable (incl. backups). Emits DataEncryptionKeyDestroyed. Idempotent on already-destroyed.
    Task<Result> DestroyKeyAsync(Guid cryptoKeyId, Guid erasureRequestId, CancellationToken cancellationToken = default);

    // Resolves every DEK to shred for a subject: Users.SubjectKeyId + EventSubjectKeys (multi-subject)
    // + tenant/platform keys bound via KeyUsageBinding. Read-only planning step for the erasure pipeline.
    Task<Result<IReadOnlyList<Guid>>> ResolveSubjectKeysAsync(Guid subjectUserId, string subjectIdHash, CancellationToken cancellationToken = default);
}
```

### 3.5 `ISurrogateKeyAnonymizer` — surrogate-key anonymization map

`NomNomzBot.Application/Services/ISurrogateKeyAnonymizer.cs`. Implements schema §5 step 1 (hash-in-place + scrub) over the surrogate FK graph. The "anonymization map" = the deterministic, keyed hash from raw Twitch id → `SubjectIdHash`, applied consistently everywhere at once. Never touches FKs (all reference `Users.Id`).

```csharp
namespace NomNomzBot.Application.Services;

public interface ISurrogateKeyAnonymizer
{
    // Computes the deterministic keyed hash (HMAC-SHA256 under a platform pepper) of a raw Twitch id.
    // Pure; the same id always maps to the same 64-hex SubjectIdHash so joins/aggregates stay linked
    // post-erasure while the person is unidentifiable. NOT reversible pseudonymization.
    Result<string> ComputeSubjectIdHash(string twitchUserId);

    // Hashes Users.TwitchUserId + every denormalized *TwitchUserId, scrubs Username/UsernameNormalized/
    // DisplayName/NickName + all snapshot columns (located via per-table (BroadcasterId,SubjectUserId)
    // indexes — indexed, not full-scan), nulls PronounId+AltPronounId ([PII-S9]), deletes the subject's
    // ViewerData (G.14) rows by ViewerUserId (per-viewer custom data is the subject's personal data), sets
    // Users.IsAnonymized=true. Runs inside the caller's transaction (no SaveChanges/commit here). Returns
    // affected table+row counts.
    Task<Result<AnonymizationReport>> AnonymizeSubjectAsync(Guid subjectUserId, string subjectIdHash, Guid? broadcasterId, CancellationToken cancellationToken = default);
}
```

### 3.6 `IConsentService` — consent / lawful-basis ledger

`NomNomzBot.Application/Services/IConsentService.cs`. Owns `ConsentRecords`. Enforces the unique `(BroadcasterId, SubjectUserId, ConsentType)` (latest-wins on the single active row).

> **Inferences never reach this ledger.** The 18+ gambling gate (`economy.md` §3.6) may pass an adult through by *inferring* adulthood from immutable Twitch facts (account age ≥ threshold, or `type ∈ {staff,admin,global_mod}`). Such an inference is recorded **only** in the `ViewerAgeConsents` (K.8) cache with `ConfirmationMethod=inferred_*` and `LawfulBasis=legitimate_interest` — it is **never** written here as a `ConsentRecords(age_18_gambling, granted, consent)` row, and `HasActiveConsentAsync` therefore returns it `false`. This keeps `ConsentRecords` meaning strictly "the human affirmatively consented".

```csharp
namespace NomNomzBot.Application.Services;

public interface IConsentService
{
    // Upserts the single active consent row to Status=granted (sets LawfulBasis/Version/Source,
    // encrypts proof-of-consent IP into IpAddressCipher under the subject DEK). Saves, emits
    // ConsentChanged. Required (Status=granted, LawfulBasis=consent) before any [PII-S9] special-category
    // use (e.g. pronouns). NOTE: age_18_gambling is regular personal data, NOT special-category — and the
    // economy 18+ toggle is optional/off-by-default over fun-money (economy.md §3.5), so it is NOT gated
    // on this; that self-confirm path uses GrantAsync only when a streamer opts the toggle in.
    Task<Result<ConsentRecordDto>> GrantAsync(GrantConsentRequest request, CancellationToken cancellationToken = default);

    // Flips the active row Status→withdrawn (sets WithdrawnAt). Saves, emits ConsentChanged. Called
    // standalone or as erasure step 5.
    Task<Result> WithdrawAsync(Guid subjectUserId, Guid? broadcasterId, string consentType, CancellationToken cancellationToken = default);

    // Deterministic read of THE active consent for the gate (e.g. age_18_gambling): true only when a
    // granted, non-expired, non-withdrawn row exists. Shape-true (reads Status+ExpiresAt+WithdrawnAt).
    Task<Result<bool>> HasActiveConsentAsync(Guid subjectUserId, Guid? broadcasterId, string consentType, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ConsentRecordDto>>> ListForSubjectAsync(Guid subjectUserId, Guid? broadcasterId, CancellationToken cancellationToken = default);
}
```

### 3.7 `IErasureService` — erasure/export/opt-out orchestrator (supersedes `IGdprService`)

`NomNomzBot.Application/Services/IErasureService.cs`. Drives the self-service my-data page. Orchestrates the schema §5 erasure procedure as one `IUnitOfWork` transaction: anonymize (3.5) → crypto-shred (3.4) → revoke auth → scrub residual IP → withdraw consent (3.6) → audit. Writes `ErasureRequest` + `ComplianceAuditLog`.

```csharp
namespace NomNomzBot.Application.Services;

public interface IErasureService
{
    // Creates an ErasureRequest (RequestType=erasure, Status=pending→running), emits
    // SubjectErasureRequested, then executes §5 steps 1-6 in ONE transaction: anonymize FK-safe,
    // DestroyKeyAsync over ResolveSubjectKeysAsync, revoke RefreshTokens/AuthSessions by UserId, scrub
    // residual IP (AuthSessions/ConsentRecords/IamAuditLog), withdraw consent, write ComplianceAuditLog
    // (SubjectIdHash/TablesAffected/RowsAffected/KeysShredded). Sets CryptoShredApplied/AnonymizationApplied,
    // Status=completed, emits SubjectErasureCompleted. On any failure: rollback, Status=failed,
    // FailureReason, emit SubjectErasureFailed. Idempotent on already-anonymized subjects.
    Task<Result<ErasureRequestDto>> RequestErasureAsync(RequestErasureRequest request, CancellationToken cancellationToken = default);

    // Creates an ErasureRequest (RequestType=export), produces machine-readable JSON (ExportFormat=json,
    // Newtonsoft.Json) of all the subject's personal data (profile, chat, redemptions, TTS usage,
    // moderation, consents — decrypting [PII-shred] via UnprotectAsync), writes it to ExportLocation,
    // emits SubjectDataExported + ComplianceAuditLog(export). Read-only w.r.t. subject data.
    Task<Result<DataExportDto>> RequestExportAsync(RequestExportRequest request, CancellationToken cancellationToken = default);

    // Opt-out (legitimate-interest withdrawal for viewers): records ErasureRequest(RequestType=opt_out),
    // withdraws marketing/leaderboard consent + flags processing opt-out, audited. No key destruction.
    Task<Result<ErasureRequestDto>> RequestOptOutAsync(RequestOptOutRequest request, CancellationToken cancellationToken = default);

    Task<Result<ErasureRequestDto>> GetRequestAsync(Guid erasureRequestId, CancellationToken cancellationToken = default);
    Task<Result<PagedList<ErasureRequestDto>>> ListRequestsAsync(PaginationParams pagination, Guid? broadcasterId, CancellationToken cancellationToken = default);
}
```

---

## 4. DTOs / contracts

`NomNomzBot.Application/Contracts/Gdpr/` (use-case DTOs) and `NomNomzBot.Application/Common/Models/Crypto/` (primitive value objects). All `record`s, Newtonsoft.Json for serialization. Enum-like fields are validated strings matching the schema `[VC:enum]` sets.

### 4.1 Crypto primitive value objects (`Common/Models/Crypto/`)

```csharp
namespace NomNomzBot.Application.Common.Models.Crypto;

public sealed record WrappedKey(string WrappedKeyMaterial, string? KekReference, string Provider);

public sealed record CipherPayload(string CipherText, string Nonce); // both base64; map to *.CipherText/*.Nonce

// AAD = tenantId‖provider‖tokenType‖keyVersion (anti-transplant). KeyVersion = CryptoKey.KeyVersion (schema Q.1).
public sealed record CipherAad(string TenantId, string Provider, string TokenType, string KeyVersion);
```

### 4.2 Request DTOs

```csharp
namespace NomNomzBot.Application.Contracts.Gdpr;

public sealed record GrantConsentRequest(
    Guid SubjectUserId, Guid? BroadcasterId, string ConsentType, string LawfulBasis,
    string? ConsentVersion, string? Source, string? ProofOfConsentIp);

public sealed record RequestErasureRequest(
    Guid SubjectUserId, Guid? BroadcasterId, string RequestedBy, string Scope);

public sealed record RequestExportRequest(
    Guid SubjectUserId, Guid? BroadcasterId, string RequestedBy);

public sealed record RequestOptOutRequest(
    Guid SubjectUserId, Guid? BroadcasterId, string RequestedBy);
```

### 4.3 Response DTOs

```csharp
namespace NomNomzBot.Application.Contracts.Gdpr;

public sealed record ConsentRecordDto(
    Guid Id, Guid? BroadcasterId, Guid SubjectUserId, string ConsentType, string Status,
    string LawfulBasis, string? ConsentVersion, DateTime GrantedAt, DateTime? WithdrawnAt, DateTime? ExpiresAt);

public sealed record ErasureRequestDto(
    Guid Id, Guid SubjectUserId, string SubjectIdHash, Guid? BroadcasterId, string RequestType,
    string RequestedBy, string Status, string Scope, bool CryptoShredApplied, bool AnonymizationApplied,
    int RowsAffected, string? ExportLocation, string? ExportFormat, string? FailureReason,
    DateTime RequestedAt, DateTime? CompletedAt);

public sealed record DataExportDto(
    Guid ErasureRequestId, string ExportFormat, string ExportLocation, long SizeBytes,
    int RowsAffected, DateTime GeneratedAt);

// Internal report shapes (service returns; not all surfaced via HTTP)
public sealed record AnonymizationReport(int TablesAffectedCount, int RowsAffected, IReadOnlyList<string> TablesAffected);
```

---

## 5. Controller endpoints

`NomNomzBot.Api/Controllers/V1/`. Two controllers. All inherit `BaseController` (returns `StatusResponseDto<T>` via `ResultResponse`). Class-level `[ApiVersion("1.0")]`.

**Role gate** — **Gate 1** is `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). **Gate 2 (management)** is `IActionAuthorizationService.AuthorizeActionAsync(userId, broadcasterId, actionKey)`, which enforces the per-route floor named in the action-key column before the service call (403 `FORBIDDEN` when below); its keys are seeded global `ActionDefinitions` (schema B.3), and a broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`. **Plane-C (platform) rows** are gated instead by `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, ...)` against the seeded global `IamPermissions` (schema C.1) — a separate vocabulary from Gate-2 `ActionDefinitions`; the ASP.NET `[Authorize(Policy="<key>")]` policy name **is** the `IamPermissions` key verbatim (e.g. `[Authorize(Policy = "audit:read")]`). The self-service `GdprController` rows are Gate-1-only, scoped to the JWT `sub` (the subject acts solely on their own data). (The legacy `[Authorize(Roles = "admin")]` attribute is the live code being replaced — it is not the gate.) Plane-C IAM permission gating per gdpr-and-data §audit.

### 5.1 `GdprController` — self-service my-data plane (subject's own data)

Route `api/v{version:apiVersion}/gdpr`. `[Authorize]` (any authenticated principal); subject id is derived from the JWT `sub` (Gate-1: never from request body), so the subject can only act on **their own** data. Tenant context from JWT.

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `GET` | `/gdpr/export` | — | `StatusResponseDto<DataExportDto>` | self (JWT `sub`) · machine-readable JSON download |
| `POST` | `/gdpr/erasure` | `RequestErasureRequest` (SubjectUserId forced to JWT `sub`) | `StatusResponseDto<ErasureRequestDto>` | self · `RequestedBy=self_service` |
| `POST` | `/gdpr/opt-out` | `RequestOptOutRequest` (self) | `StatusResponseDto<ErasureRequestDto>` | self |
| `GET` | `/gdpr/requests` | `[FromQuery] PageRequestDto` | `PaginatedResponse<ErasureRequestDto>` | self · own requests only |
| `GET` | `/gdpr/requests/{id:guid}` | — | `StatusResponseDto<ErasureRequestDto>` | self · own request |
| `GET` | `/gdpr/consents` | — | `StatusResponseDto<IReadOnlyList<ConsentRecordDto>>` | self |
| `POST` | `/gdpr/consents` | `GrantConsentRequest` (self) | `StatusResponseDto<ConsentRecordDto>` | self |
| `DELETE` | `/gdpr/consents/{consentType}` | — | `StatusResponseDto<object>` | self (withdraw) |

### 5.2 `ComplianceController` — operator/admin plane (Plane-C IAM)

Route `api/v{version:apiVersion}/compliance`. All rows are **platform** (Plane-C IAM), gated by the named permission key per the role-gate preamble above. Broadcaster-initiated erasure (controller of their channel). Cross-tenant/privileged actions are themselves audited (`ComplianceAuditLog` / `IamAuditLog`).

| Verb | Route | Request DTO | Response DTO | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `POST` | `/compliance/erasure` | `RequestErasureRequest` (`RequestedBy=broadcaster`\|`platform_iam`) | `StatusResponseDto<ErasureRequestDto>` | platform · `tenant:access` (broadcaster-of-channel) |
| `GET` | `/compliance/erasure` | `[FromQuery] PageRequestDto` | `PaginatedResponse<ErasureRequestDto>` | platform · `audit:read` |

---

## 6. Pipeline actions

**None.** This subsystem exposes no `ICommandAction`. Consent gating consumed by other pipeline actions (e.g. an economy gambling action) is read via `IConsentService.HasActiveConsentAsync` from within *those* actions; it is not itself a pipeline block. Crypto is invoked by integration/token services, not by the pipeline engine.

---

## 7. DI registration

In `NomNomzBot.Infrastructure/DependencyInjection.cs` (`AddInfrastructure`), beside the existing `// Security` and `// GDPR` blocks. Lifetimes match the existing pattern: stateless crypto primitives = singleton; row-touching use-case services = scoped (consume `IApplicationDbContext`/`IUnitOfWork`).

```csharp
// Crypto primitives (stateless, singleton — in-box System.Security.Cryptography)
services.AddSingleton<IFieldCipher, AesGcmFieldCipher>();
services.AddSingleton<IKdf, HkdfSha256Kdf>();

// Key-vault adapter — profile-selected by DeploymentProfile.TokenVault
//   local_aes  -> root KEK custodied in the OS-native secure store (DPAPI / Keychain / libsecret),
//                 encrypted-file (0600) or env-KEK fallback only when headless   (lite / self-host default)
//   kms_envelope -> Azure.Security.KeyVault.Keys (SaaS; EU Managed-HSM)  -- loaded ONLY in this branch
if (profile.TokenVault == TokenVaultKind.KmsEnvelope)               // profile = resolved DeploymentProfileSnapshot
    services.AddSingleton<IKeyVault, AzureKeyVaultKeyVault>();   // Azure.Security.KeyVault.Keys 4.10.0
else
    services.AddSingleton<IKeyVault, OsSecureStoreKeyVault>();   // zero 3rd-party in the lite binary

// DEK lifecycle + GDPR use-case services (scoped — DbContext/UnitOfWork)
services.AddScoped<ISubjectKeyService, SubjectKeyService>();
services.AddScoped<ISurrogateKeyAnonymizer, SurrogateKeyAnonymizer>();
services.AddScoped<IConsentService, ConsentService>();
services.AddScoped<IErasureService, ErasureService>();           // supersedes IGdprService registration

// IEncryptionService retained transitionally for re-encryption migration read path, then removed.
```

**Deployment-profile adapter variants** (`IKeyVault`): `OsSecureStoreKeyVault` (lite/self-host — wraps the per-tenant/subject DEKs with a local root KEK whose custody is the **OS-native secure store by default**: Windows Credential Locker/DPAPI · macOS Keychain · Linux libsecret; falls back to an encrypted-file (0600) or operator-provided env KEK **only** on headless hosts with no OS keystore) vs `AzureKeyVaultKeyVault` (SaaS — `WrapKey`/`UnwrapKey` Managed-HSM). Selected by `DeploymentProfile.TokenVault` exactly as DB provider / cache / executor are selected. The `kms_envelope` branch is the **only** place the Azure SDK is referenced, so the lite binary carries zero crypto 3rd-parties. **Only the root KEK is OS-custodied; the DEKs + ciphertext live in the DB** — multi-tenant token storage and per-subject crypto-shred require this — so OS custody (root key) and the AES-256-GCM-under-DEK data plane **compose** (envelope encryption), they are not alternatives.

---

## 8. Dependencies (stack doc)

| Dependency | Party | Used for |
|---|---|---|
| `System.Security.Cryptography` (`AesGcm` w/ `tagSizeInBytes:16`, `HKDF`, `RandomNumberGenerator`, `HMACSHA256`, `CryptographicOperations.ZeroMemory`) | 1st/2nd (in-box .NET 10) | `IFieldCipher`, `IKdf`, DEK generation, `SubjectIdHash`, secret zeroization. Glue only — primitives are not hand-rolled. |
| `Azure.Security.KeyVault.Keys` 4.10.0 | 2nd (MIT) | `AzureKeyVaultKeyVault` (SaaS `kms_envelope` only) — KEK `WrapKey`/`UnwrapKey`, EU Managed-HSM. |
| `System.Security.Cryptography.ProtectedData` 10.0.9 | 2nd (MIT) | The **Windows** OS-native KEK-custody backend in `OsSecureStoreKeyVault` (DPAPI, machine-bound) — the **default** on Windows self-host. macOS Keychain / Linux libsecret are the other backends (P/Invoke, no NuGet); an encrypted-file (0600) or env KEK is the headless fallback. |
| `Microsoft.EntityFrameworkCore` 10.0.9 (+ Npgsql / Sqlite provider per profile) | 2nd / 3rd-adjacent | Persistence of all §1 entities via `IApplicationDbContext` + `IUnitOfWork`; EF10 named query filters for soft-delete/tenant. |
| Newtonsoft.Json | (project app-JSON convention) | Export document serialization (`DataExportDto`), `[VC:JSON]` columns (`ComplianceAuditLog.TablesAffected`). |
| `Microsoft.Extensions.Logging` (`ILogger` + `[LoggerMessage]`) + OpenTelemetry | 2nd | Structured audit logging; **never log raw ids/usernames/tokens** — `SubjectIdHash` only. |
| `NomNomzBot.Domain.Interfaces.IEventBus` (in-house) | 1st | Publishing §2 domain events (no MediatR). |

No new 3rd-party dependency beyond what the stack doc already accepts. AES-CBC/`SHA256(rawKey)` of the legacy `EncryptionService` is **removed**.

---

## 9. In-chat self-service erasure (no public HTTP surface)

The §5.1 `GdprController` requires an authenticated principal (JWT `sub`) — but **viewers never sign up**, and a self-host operator may not expose the bot to the internet at all. So the same rights are exercisable **in chat**, where the issuer is authenticated by Twitch itself (the verified `user-id` IRC tag / EventSub `chatter_user_id`). This is a thin **alternative surface over the same `IErasureService` use cases** — not a parallel implementation.

**Built-in reserved commands** — resolved by the command engine **before any authored command** (`commands-pipelines.md` §3.2 precedence): a streamer **cannot shadow, override, or disable** them. The data-subject rights floor is always-on (per opt-in/default-deny).

| Trigger (+ aliases) | Maps to | Notes |
|---|---|---|
| `!forgetme` (`!gdpr forget`) | `IErasureService.RequestErasureAsync` (`RequestedBy=self_service`) | Two-step: first call replies with a per-subject confirm token (60s TTL, in-memory); `!forgetme confirm` executes. Irreversible (crypto-shred). |
| `!mydata` (`!gdpr export`) | `IErasureService.RequestExportAsync` | Can't dump JSON into chat: if internet-exposed the bot **whispers a one-time short-TTL link**; on a no-HTTP self-host it records the request and the **operator** (the controller on self-host) furnishes the file. |
| `!gdpr status` | `IErasureService.GetRequestAsync` (subject's latest) | Read-only. |

**Identity & safety (free, by construction):**
- The subject is the **Twitch-verified issuer** — a command can only ever touch the issuer's *own* data. No `!forgetme @someone`. A mod/broadcaster erasing *another* viewer stays on the audited `ComplianceController` plane (a controller action), never a chat command.
- **Moderation carve-out (MANDATORY).** Erasure MUST NOT clear an active ban/timeout. Art. 17(3) lets the controller retain data needed to enforce safety / defend claims. The §3.7 procedure wipes stats/economy/logs/PII and crypto-shreds the subject DEK **but preserves the moderation record + its minimal justification** (`moderation.md`). Without this, `!forgetme` is one-word ban-evasion.

**Re-entry is the model — no standing opt-out tombstone.** Erasure is point-in-time. After `DestroyKeyAsync` (§3.4) shreds the subject DEK, the subject's prior PII is permanently unreadable (events + backups included), and the bot keeps **no** "this person was forgotten" flag. On the viewer's next chat message or redeem, `GetOrCreateSubjectKeyAsync` (§3.4) finds the prior key `destroyed` (not `active`) and **mints a fresh DEK** → the viewer re-enters as a clean slate; the shredded history stays dead (a new key never resurrects an old one). This is the *natural* behavior of the crypto-shred design — **nothing extra to build, no new schema** (reuses `ErasureRequest(RequestType=erasure)` + key-shred + the §3.5 anonymize/scrub).

**Mandatory informed-re-entry reply.** Because re-entry is automatic, the completion reply MUST state it — that explicit notice is what makes resumed processing **informed** (and so lawful under the legitimate-interest viewer basis, `2026-06-16-gdpr-and-data.md`). The reply has **two parts, split by who owns them:**

1. **The friendly completion copy is customizable by the streamer/brand.** The "done, you're a clean slate" sentence is a streamer-/brand-configurable response template (personality is a product value — they may restyle the sass, swap emoji, translate it). The bot exposes this wording as configurable per channel; a streamer who sets nothing gets the default copy below.
2. **The informed-re-entry clause is MANDATORY and non-removable.** The *"any new message or redeem puts you back in the system"* notice is a **required element the bot always appends** to the configured copy — it is not part of the editable template, cannot be shortened, removed, or disabled, and is the part that makes resumed processing legally informed. The informed-re-entry property therefore holds regardless of how the streamer styles part 1.

Default copy (part 1, customizable) with the always-appended mandatory clause (part 2, fixed):

> 🧹 Done — your data's been sanitized, you're a clean slate. ⚠️ Sending another message or redeeming a reward will put you back into the system. Stay quiet if you want to stay anonymous.

## 10. Decisions (resolved)

- **Crypto-shred completeness.** O(1) ciphertext crypto-shred is the in-scope erasure mechanism. Plaintext `[PII-scrub]` snapshot row-level erasure (`ISurrogateKeyAnonymizer.AnonymizeSubjectAsync`) and multi-subject `EventSubjectKeys` handling are part of this subsystem and are fully specified here. Both ship as part of the erasure pipeline (§3.5, §3.7).
- **`ITenantScoped` widens to `Guid`.** This subsystem is written against `Guid` `BroadcasterId`, per the locked schema (§1.1). The widening is the one-time rebuild change owned by the persistence/tenancy slice; it is a build dependency of these signatures.
- **KEK rotation cadence / DataProtection key-ring.** Rotation scheduling is owned by the auth/persistence slices. `IKeyVault` exposes wrap/unwrap only and is intentionally agnostic to KEK rotation cadence; that is a dependency on the auth/persistence slices, not a property of this subsystem.

---

## As-built — erasure pipeline + controllers (2026-07-17, item 23 slice B)

- `GdprController` (Gate-1, subject ALWAYS the JWT sub) + `ComplianceController` (Plane-C:
  `tenant:access` / `audit:read` policies) shipped per §5; the legacy `UsersController`
  `{userId}/data-export` + `{userId}/data` routes and `IGdprService`/`GdprService` are RETIRED —
  their three proven behaviors (vault revocation targets only the subject, cross-channel ViewerData
  scrub, bare-profile erasure) live on in `ErasureServiceTests`.
- **Two-phase failure semantics:** the `ErasureRequest` row is written in its own save (phase 1);
  the destructive pipeline runs in ONE `IUnitOfWork` tx with the completed status + `ComplianceAuditLog`
  row inside it (phase 2 — neither exists without the other); on failure the tx rolls back,
  `ChangeTracker.Clear()`, and the surviving request is stamped `failed` with an `Outcome=failed`
  audit row in a separate save. The request row is always queryable.
- `ErasureRequest.SubjectKeyId` is nullable (not every subject holds a DEK); the row carries
  `ReportJson` (the AnonymizationReport). `DataExportDto` carries the Newtonsoft-serialized export
  inline in a `Document` field (no file store); `ListRequestsAsync` gained a `subjectUserId`
  self-scoping filter.
- Consent IPs are NOT sealed (the `ConsentRecord` entity's own doc-comment defers
  `IpAddressCipher`; `ProofOfConsentIp` is accepted but unpersisted — proportionate privacy).
- Opt-out = withdraw marketing/leaderboard consents + `ViewerProfile.IsAnalyticsOptedOut`
  cross-tenant, audited as `consent_change`.
- Crypto-shred resolves `Users.SubjectKeyId` + active subject-scope `CryptoKey` rows by
  `SubjectIdHash`; the full §3.4 widening (`ResolveSubjectKeysAsync`, tenant/platform keys,
  rotation, `KeyUsageBinding`, `EventSubjectKeys`) is deferred to a later crypto slice.
  `DeletionAuditLog` remains in place read-only; new writes go to `ComplianceAuditLog`.
- Domain events (`Identity/Events/GdprEvents.cs`): SubjectErasureRequested/Completed/Failed,
  SubjectDataExported, ConsentChanged — hashed subject only.
