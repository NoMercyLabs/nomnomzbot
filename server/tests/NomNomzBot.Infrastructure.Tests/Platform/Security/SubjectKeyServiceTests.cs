// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// End-to-end envelope-encryption tests over the real stack: AES-256-GCM field cipher + OS-store key vault
/// (deterministic config-KEK path) + the persisted DEK registry (<see cref="CryptoKeySubjectKeyStore"/>). Proves
/// the four mandatory security behaviors through the full DEK-wrapped-by-KEK envelope, the crypto-shred GDPR
/// guarantee, AND that the wrapped DEK survives a process restart (the token-vault decrypt-after-restart bug).
/// </summary>
public class SubjectKeyServiceTests
{
    // A fixed base64 32-byte deployment key drives the deterministic KEK fallback (no OS keystore needed).
    private const string ConfigKey = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh"; // 32 bytes, base64

    // A different 32-byte base64 deployment key — the test analogue of a rotated/replaced ENCRYPTION_KEY: DEKs
    // wrapped under ConfigKey no longer unwrap under this one.
    private const string RotatedKey = "QmFyLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";

    private static ISubjectKeyService BuildOver(string databaseName) =>
        BuildOver(databaseName, ConfigKey);

    private static ISubjectKeyService BuildOver(string databaseName, string configKey)
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = configKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        // A fresh DbContext over the SAME named in-memory store = a fresh service instance over the SAME
        // persisted database — the test analogue of an API restart.
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(
            AuthTestBuilder.NewContext(databaseName)
        );
        return new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
    }

    private static (ISubjectKeyService Service, ISubjectKeyStore Store) Build()
    {
        (ISubjectKeyService service, ISubjectKeyStore store, _) = BuildWithDb();
        return (service, store);
    }

    /// <summary>Same wiring as <see cref="Build"/>, also exposing the backing context so tests can assert
    /// the persisted registry side effects (usage bindings, event-subject mappings) directly.</summary>
    private static (
        ISubjectKeyService Service,
        ISubjectKeyStore Store,
        AuthDbContext Db
    ) BuildWithDb()
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = ConfigKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        AuthDbContext db = AuthTestBuilder.NewContext();
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(db);
        ISubjectKeyService service = new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
        return (service, store, db);
    }

    private static CipherAad Aad(string tenant = "tenant-A") =>
        new(tenant, "twitch", "access", "1");

    // The Q.2 resource coordinate every seal in these tests declares (asserted in the binding tests).
    private const string Table = "IntegrationTokens";
    private const string Column = "CipherText";

    private static async Task<Guid> NewSubjectKeyAsync(ISubjectKeyService service)
    {
        Result<Guid> keyId = await service.GetOrCreateSubjectKeyAsync(
            Guid.CreateVersion7(),
            subjectIdHash: Guid.NewGuid().ToString("N"),
            CancellationToken.None
        );
        keyId.IsSuccess.Should().BeTrue();
        return keyId.Value;
    }

    // ─── 1. Round-trip through the envelope ────────────────────────────────────────

    [Fact]
    public async Task ProtectThenUnprotect_ReturnsOriginalPlaintext()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);
        const string secret = "twitch-refresh-token-7f3a";

        Result<CipherPayload> sealed_ = await service.ProtectAsync(
            keyId,
            secret,
            Aad(),
            Table,
            Column
        );
        sealed_.IsSuccess.Should().BeTrue();

        Result<string> opened = await service.UnprotectAsync(keyId, sealed_.Value, Aad());

        opened.IsSuccess.Should().BeTrue();
        opened.Value.Should().Be(secret);
    }

    [Fact]
    public async Task GetOrCreateSubjectKey_StoresWrappedDekNeverPlaintext()
    {
        (ISubjectKeyService service, ISubjectKeyStore store) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);

        SubjectKeyRecord? record = await store.GetAsync(keyId);

        record.Should().NotBeNull();
        record!.Status.Should().Be(SubjectKeyStatus.Active);
        record.Provider.Should().Be("local_aes");
        record.Algorithm.Should().Be("AES-256-GCM");
        // The persisted material is the WRAPPED DEK, not a raw 32-byte key: base64-decoded it is
        // nonce(12)+cipher(32)+tag(16) = 60 bytes, never the bare 32.
        record.WrappedKeyMaterial.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(record.WrappedKeyMaterial!).Length.Should().Be(60);
    }

    // ─── 2. AAD non-transplantability through the service ──────────────────────────

    [Fact]
    public async Task Unprotect_WithDifferentAad_FailsClosed()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);

        Result<CipherPayload> sealed_ = await service.ProtectAsync(
            keyId,
            "secret",
            Aad("tenant-A"),
            Table,
            Column
        );

        // Same DEK, different subject/field context ⇒ tag check fails, no plaintext.
        Result<string> opened = await service.UnprotectAsync(keyId, sealed_.Value, Aad("tenant-B"));

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("DECRYPT_FAILED");
    }

    [Fact]
    public async Task Ciphertext_CannotBeOpenedUnderAnotherSubjectsKey()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyA = await NewSubjectKeyAsync(service);
        Guid keyB = await NewSubjectKeyAsync(service);

        Result<CipherPayload> sealedForA = await service.ProtectAsync(
            keyA,
            "subject-A-secret",
            Aad(),
            Table,
            Column
        );

        // A blob sealed under subject A's DEK cannot be opened under subject B's DEK (different DEK).
        Result<string> openedUnderB = await service.UnprotectAsync(keyB, sealedForA.Value, Aad());

        openedUnderB.IsFailure.Should().BeTrue();
    }

    // ─── 3. Tamper detection through the service ───────────────────────────────────

    [Fact]
    public async Task Unprotect_WithTamperedCiphertext_FailsClosed()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);
        Result<CipherPayload> sealed_ = await service.ProtectAsync(
            keyId,
            "value",
            Aad(),
            Table,
            Column
        );

        byte[] blob = Convert.FromBase64String(sealed_.Value.CipherText);
        blob[0] ^= 0x01;
        CipherPayload tampered = sealed_.Value with { CipherText = Convert.ToBase64String(blob) };

        Result<string> opened = await service.UnprotectAsync(keyId, tampered, Aad());

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("DECRYPT_FAILED");
    }

    // ─── 4. Crypto-shred ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DestroyKey_RendersExistingCiphertextPermanentlyUnrecoverable()
    {
        (ISubjectKeyService service, ISubjectKeyStore store) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);
        Result<CipherPayload> sealed_ = await service.ProtectAsync(
            keyId,
            "to-be-shredded",
            Aad(),
            Table,
            Column
        );

        // Sanity: readable before the shred.
        (await service.UnprotectAsync(keyId, sealed_.Value, Aad()))
            .Value.Should()
            .Be("to-be-shredded");

        Guid erasureRequestId = Guid.CreateVersion7();
        Result shred = await service.DestroyKeyAsync(keyId, erasureRequestId);
        shred.IsSuccess.Should().BeTrue();

        // The wrapped DEK is gone and the record is marked destroyed.
        SubjectKeyRecord? record = await store.GetAsync(keyId);
        record!.Status.Should().Be(SubjectKeyStatus.Destroyed);
        record.WrappedKeyMaterial.Should().BeNull();
        record.DestroyedAt.Should().NotBeNull();
        record.ErasureRequestId.Should().Be(erasureRequestId);

        // The very same ciphertext can no longer be decrypted — the GDPR guarantee, surfaced as a
        // closed failure (not an exception, not silent plaintext).
        Result<string> afterShred = await service.UnprotectAsync(keyId, sealed_.Value, Aad());
        afterShred.IsFailure.Should().BeTrue();
        afterShred.ErrorCode.Should().Be("KEY_DESTROYED");
    }

    [Fact]
    public async Task DestroyKey_IsIdempotent()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);
        Guid erasureRequestId = Guid.CreateVersion7();

        (await service.DestroyKeyAsync(keyId, erasureRequestId)).IsSuccess.Should().BeTrue();
        // A second shred of an already-destroyed key is a no-op success, not an error.
        (await service.DestroyKeyAsync(keyId, erasureRequestId))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public async Task ShreddedSubject_ReEntersWithFreshKey_NotResurrected()
    {
        (ISubjectKeyService service, _) = Build();
        Guid subjectUserId = Guid.CreateVersion7();
        string subjectIdHash = Guid.NewGuid().ToString("N");

        Guid firstKey = (
            await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;
        await service.DestroyKeyAsync(firstKey, Guid.CreateVersion7());

        // Same subject identity returns AFTER a shred: a brand-new DEK is minted, the dead one is not reused.
        Guid secondKey = (
            await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;

        secondKey.Should().NotBe(firstKey);
    }

    [Fact]
    public async Task GetOrCreateSubjectKey_IsIdempotentForActiveSubject()
    {
        (ISubjectKeyService service, _) = Build();
        Guid subjectUserId = Guid.CreateVersion7();
        string subjectIdHash = Guid.NewGuid().ToString("N");

        Guid first = (await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)).Value;
        Guid second = (
            await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;

        second.Should().Be(first);
    }

    // ─── 5. Restart survival — the token-vault decrypt-after-restart regression ──────

    [Fact]
    public async Task DekMintedInOneProcess_UnwrapsAndDecrypts_InTheNext()
    {
        // The exact failure the bug produced: a key minted + a token sealed before a restart, then read after.
        string database = Guid.NewGuid().ToString();
        const string secret = "twitch-access-token-after-restart";

        // ── First process: mint a DEK, seal a token under it, then the process ends. ──
        Guid keyId;
        CipherPayload sealed_;
        {
            ISubjectKeyService first = BuildOver(database);
            keyId = (
                await first.GetOrCreateSubjectKeyAsync(
                    Guid.CreateVersion7(),
                    subjectIdHash: "subject-hash-persisted"
                )
            ).Value;
            Result<CipherPayload> protect = await first.ProtectAsync(
                keyId,
                secret,
                Aad(),
                Table,
                Column
            );
            protect.IsSuccess.Should().BeTrue();
            sealed_ = protect.Value;
        }

        // ── Second process: a brand-new service over a brand-new context, same persisted store. ──
        ISubjectKeyService second = BuildOver(database);

        // The DEK record is found (no KEY_NOT_FOUND) and the ciphertext sealed before the "restart" opens.
        Result<string> opened = await second.UnprotectAsync(keyId, sealed_, Aad());

        opened.IsSuccess.Should().BeTrue("the wrapped DEK must persist across the restart");
        opened.Value.Should().Be(secret);
    }

    [Fact]
    public async Task GetOrCreateSubjectKey_ResolvesToSameKey_AcrossAProcessRestart()
    {
        // The vault re-derives the key id from the stable subject identity each boot — that re-resolution must
        // find the persisted record, not mint a second (orphaning the ciphertext sealed under the first).
        string database = Guid.NewGuid().ToString();
        Guid subjectUserId = Guid.CreateVersion7();
        const string subjectIdHash = "stable-subject-identity-hash";

        Guid before = (
            await BuildOver(database).GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;

        Guid after = (
            await BuildOver(database).GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;

        after
            .Should()
            .Be(before, "the persisted active key is reused, not re-minted, after a restart");
    }

    // ─── 7. KEK rotation — a write self-heals a stale DEK; a shredded key stays dead ──

    [Fact]
    public async Task ProtectAfterKekRotation_RekeysInPlaceAndSucceeds()
    {
        // The stale-secret failure: a DEK minted + a secret sealed under one KEK, then the deployment's KEK is
        // rotated. Reads of the old secret are dead, but a WRITE of a new secret must self-heal — re-key the
        // subject in place under the current KEK and seal the new value, readable thereafter.
        string database = Guid.NewGuid().ToString();
        Guid subjectUserId = Guid.CreateVersion7();
        const string subjectIdHash = "kek-rotation-subject";

        Guid keyId;
        CipherPayload oldSealed;
        {
            ISubjectKeyService underKekA = BuildOver(database, ConfigKey);
            keyId = (
                await underKekA.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
            ).Value;
            Result<CipherPayload> sealedOld = await underKekA.ProtectAsync(
                keyId,
                "old-secret",
                Aad(),
                Table,
                Column
            );
            sealedOld.IsSuccess.Should().BeTrue();
            oldSealed = sealedOld.Value;
        }

        // Same store, ROTATED KEK: the old DEK no longer unwraps.
        ISubjectKeyService underKekB = BuildOver(database, RotatedKey);

        // The old ciphertext is unrecoverable (its KEK is gone) — the read failure the user sees today.
        (await underKekB.UnprotectAsync(keyId, oldSealed, Aad()))
            .IsFailure.Should()
            .BeTrue("the secret sealed under the lost KEK cannot be read");

        // A WRITE self-heals: re-keys the same subject in place and seals the new value under the current KEK.
        Result<CipherPayload> reSealed = await underKekB.ProtectAsync(
            keyId,
            "new-secret",
            Aad(),
            Table,
            Column
        );
        reSealed
            .IsSuccess.Should()
            .BeTrue("a write must succeed by re-keying the stale DEK under the current KEK");

        // The freshly-sealed secret round-trips under the current KEK.
        Result<string> opened = await underKekB.UnprotectAsync(keyId, reSealed.Value, Aad());
        opened.IsSuccess.Should().BeTrue();
        opened.Value.Should().Be("new-secret");
    }

    [Fact]
    public async Task ProtectAfterKekRotation_DoesNotResurrectACryptoShreddedKey()
    {
        // A crypto-shredded key STAYS dead even under a rotated KEK: a write fails closed (KEY_DESTROYED), never
        // silently re-keyed — the GDPR erasure guarantee outranks the stale-secret self-heal.
        string database = Guid.NewGuid().ToString();
        Guid subjectUserId = Guid.CreateVersion7();
        const string subjectIdHash = "shredded-subject";

        Guid keyId;
        {
            ISubjectKeyService underKekA = BuildOver(database, ConfigKey);
            keyId = (
                await underKekA.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
            ).Value;
            (await underKekA.DestroyKeyAsync(keyId, Guid.CreateVersion7()))
                .IsSuccess.Should()
                .BeTrue();
        }

        ISubjectKeyService underKekB = BuildOver(database, RotatedKey);
        Result<CipherPayload> write = await underKekB.ProtectAsync(
            keyId,
            "should-not-write",
            Aad(),
            Table,
            Column
        );

        write.IsFailure.Should().BeTrue();
        write
            .ErrorCode.Should()
            .Be("KEY_DESTROYED", "a shredded key is never re-keyed, even under a rotated KEK");
    }

    // ─── 6. Negative — a truly-missing subject still fails closed ────────────────────

    [Fact]
    public async Task Unprotect_ForMissingKey_FailsWithKeyNotFound()
    {
        (ISubjectKeyService service, _) = Build();

        // No key was ever minted for this id — the store returns nothing, so the service fails closed.
        Result<CipherPayload> sealed_ = await service.ProtectAsync(
            Guid.CreateVersion7(),
            "value",
            Aad(),
            Table,
            Column
        );
        sealed_.IsFailure.Should().BeTrue();
        sealed_.ErrorCode.Should().Be("KEY_NOT_FOUND");
    }

    [Fact]
    public async Task Decrypt_AfterRestart_ForKeyThatNeverExisted_FailsWithKeyNotFound()
    {
        string database = Guid.NewGuid().ToString();
        // Persist ONE real key so the store/database exists, then ask the next process for a different id.
        await BuildOver(database).GetOrCreateSubjectKeyAsync(Guid.CreateVersion7(), "some-subject");

        ISubjectKeyService next = BuildOver(database);
        Result<string> opened = await next.UnprotectAsync(
            Guid.CreateVersion7(), // an id that was never minted
            new CipherPayload(Convert.ToBase64String([1, 2, 3]), Convert.ToBase64String([4, 5, 6])),
            Aad()
        );

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("KEY_NOT_FOUND");
    }

    // ─── 8. Tenant / platform key scopes (§3.4 widening) ────────────────────────────

    [Fact]
    public async Task TenantKey_IsIdempotentPerBroadcaster_AndDistinctAcrossBroadcasters()
    {
        (ISubjectKeyService service, ISubjectKeyStore store) = Build();
        Guid broadcasterA = Guid.CreateVersion7();
        Guid broadcasterB = Guid.CreateVersion7();

        Guid first = (await service.GetOrCreateTenantKeyAsync(broadcasterA)).Value;
        Guid again = (await service.GetOrCreateTenantKeyAsync(broadcasterA)).Value;
        Guid other = (await service.GetOrCreateTenantKeyAsync(broadcasterB)).Value;

        again.Should().Be(first, "one tenant DEK per broadcaster");
        other.Should().NotBe(first, "each broadcaster gets its own tenant DEK");

        SubjectKeyRecord? record = await store.GetAsync(first);
        record!.KeyScope.Should().Be("tenant");
        record.BroadcasterId.Should().Be(broadcasterA);
        record.SubjectIdHash.Should().BeNull();
        record.Status.Should().Be(SubjectKeyStatus.Active);
    }

    [Fact]
    public async Task PlatformKey_IsIdempotentPerPurpose()
    {
        (ISubjectKeyService service, ISubjectKeyStore store) = Build();

        Guid first = (await service.GetOrCreatePlatformKeyAsync("app_settings")).Value;
        Guid again = (await service.GetOrCreatePlatformKeyAsync("app_settings")).Value;
        Guid other = (await service.GetOrCreatePlatformKeyAsync("federation_seal")).Value;

        again.Should().Be(first, "one platform DEK per purpose");
        other.Should().NotBe(first, "each purpose gets its own platform DEK");

        SubjectKeyRecord? record = await store.GetAsync(first);
        record!.KeyScope.Should().Be("platform");
        record.BroadcasterId.Should().BeNull();
        record.SubjectIdHash.Should().Be("app_settings"); // the purpose IS the registry identity
    }

    [Fact]
    public async Task PlatformKey_RejectsAnUnboundedPurpose()
    {
        (ISubjectKeyService service, _) = Build();

        (await service.GetOrCreatePlatformKeyAsync("")).ErrorCode.Should().Be("VALIDATION_FAILED");
        (await service.GetOrCreatePlatformKeyAsync(new string('x', 65)))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED", "the purpose occupies the string(64) SubjectIdHash slot");
    }

    // ─── 9. Usage bindings (Q.2) — every seal inventories its resource ──────────────

    [Fact]
    public async Task Protect_RecordsAKeyUsageBinding_OncePerResource()
    {
        (ISubjectKeyService service, _, AuthDbContext db) = BuildWithDb();
        Guid keyId = await NewSubjectKeyAsync(service);

        (await service.ProtectAsync(keyId, "v1", Aad(), Table, Column)).IsSuccess.Should().BeTrue();
        // Re-sealing the SAME resource asserts, not duplicates, the binding.
        (await service.ProtectAsync(keyId, "v2", Aad(), Table, Column))
            .IsSuccess.Should()
            .BeTrue();
        // A different column under the same DEK is a second inventory row.
        (await service.ProtectAsync(keyId, "v3", Aad(), Table, "Nonce"))
            .IsSuccess.Should()
            .BeTrue();

        List<KeyUsageBinding> bindings = await db
            .KeyUsageBindings.AsNoTracking()
            .Where(b => b.CryptoKeyId == keyId)
            .ToListAsync();
        bindings.Should().HaveCount(2);
        bindings
            .Should()
            .ContainSingle(b => b.ResourceTable == Table && b.ResourceColumn == Column);
        bindings
            .Should()
            .ContainSingle(b => b.ResourceTable == Table && b.ResourceColumn == "Nonce");
    }

    // ─── 10. Rotation (§3.4) — generations link, old data reads, new writes move on ──

    [Fact]
    public async Task Rotate_LinksGenerations_RetiresTheOld_AndOldCiphertextStillDecrypts()
    {
        (ISubjectKeyService service, ISubjectKeyStore store) = Build();
        Guid subjectUserId = Guid.CreateVersion7();
        const string subjectIdHash = "rotation-subject";
        Guid oldKeyId = (
            await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;
        const string secret = "sealed-before-rotation";
        CipherPayload sealedOld = (
            await service.ProtectAsync(oldKeyId, secret, Aad(), Table, Column)
        ).Value;

        Result<Guid> rotated = await service.RotateKeyAsync(oldKeyId);

        rotated.IsSuccess.Should().BeTrue(rotated.ErrorMessage);
        Guid newKeyId = rotated.Value;
        newKeyId.Should().NotBe(oldKeyId);

        // The predecessor retired to `rotating`, its wrapped material RETAINED (rotation ≠ shred).
        SubjectKeyRecord? old = await store.GetAsync(oldKeyId);
        old!.Status.Should().Be(SubjectKeyStatus.Rotating);
        old.WrappedKeyMaterial.Should().NotBeNull();

        // The successor is the identity's sole active key, version bumped, lineage linked.
        SubjectKeyRecord? successor = await store.GetAsync(newKeyId);
        successor!.Status.Should().Be(SubjectKeyStatus.Active);
        successor.RotatedFromKeyId.Should().Be(oldKeyId);
        successor.KeyVersion.Should().Be("2");
        successor.KeyScope.Should().Be("subject");
        successor.SubjectIdHash.Should().Be(subjectIdHash);

        // Data written under the old generation still decrypts (lazy re-encryption — readers keep the
        // old key id in their envelope until the resource owner re-seals).
        Result<string> openedOld = await service.UnprotectAsync(oldKeyId, sealedOld, Aad());
        openedOld.IsSuccess.Should().BeTrue(openedOld.ErrorMessage);
        openedOld.Value.Should().Be(secret);

        // But the retired generation never seals NEW data...
        Result<CipherPayload> writeOld = await service.ProtectAsync(
            oldKeyId,
            "new-secret",
            Aad(),
            Table,
            Column
        );
        writeOld.ErrorCode.Should().Be("KEY_NOT_ACTIVE");

        // ...and the identity now resolves to the successor for every new write.
        (await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash))
            .Value.Should()
            .Be(newKeyId);
    }

    [Fact]
    public async Task Rotate_CarriesUsageBindingsToTheSuccessor()
    {
        (ISubjectKeyService service, _, AuthDbContext db) = BuildWithDb();
        Guid oldKeyId = await NewSubjectKeyAsync(service);
        (await service.ProtectAsync(oldKeyId, "value", Aad(), Table, Column))
            .IsSuccess.Should()
            .BeTrue();

        Guid newKeyId = (await service.RotateKeyAsync(oldKeyId)).Value;

        // The successor inherits the inventory: the planner knows which resources it covers going forward.
        List<KeyUsageBinding> successorBindings = await db
            .KeyUsageBindings.AsNoTracking()
            .Where(b => b.CryptoKeyId == newKeyId)
            .ToListAsync();
        successorBindings
            .Should()
            .ContainSingle(b => b.ResourceTable == Table && b.ResourceColumn == Column);
    }

    [Fact]
    public async Task Rotate_RefusesADestroyedKey()
    {
        (ISubjectKeyService service, _) = Build();
        Guid keyId = await NewSubjectKeyAsync(service);
        (await service.DestroyKeyAsync(keyId, Guid.CreateVersion7())).IsSuccess.Should().BeTrue();

        Result<Guid> rotated = await service.RotateKeyAsync(keyId);

        rotated.IsFailure.Should().BeTrue();
        rotated
            .ErrorCode.Should()
            .Be("KEY_DESTROYED", "a shredded key is never brought back to life");
    }

    // ─── 11. Resolve (§3.4) — the erasure planner's complete shred set ──────────────

    [Fact]
    public async Task ResolveSubjectKeys_ReturnsEveryGeneration_TheUsersFk_AndEventMappedKeys()
    {
        (ISubjectKeyService service, _, AuthDbContext db) = BuildWithDb();
        Guid subjectUserId = Guid.CreateVersion7();
        const string subjectIdHash = "resolve-subject";

        // Generation 1 + its rotated successor, both registered under the subject's hash.
        Guid firstKey = (
            await service.GetOrCreateSubjectKeyAsync(subjectUserId, subjectIdHash)
        ).Value;
        Guid successorKey = (await service.RotateKeyAsync(firstKey)).Value;

        // A key FK'd from the user row under a DIFFERENT registry hash (the TokenProtector derivation).
        CryptoKey fkKey = new()
        {
            KeyScope = "subject",
            SubjectIdHash = "token-protector-derived-hash",
            WrappedKeyMaterial = "wrapped",
            Provider = "local_aes",
            Algorithm = "AES-256-GCM",
            Status = "active",
        };
        db.CryptoKeys.Add(fkKey);
        db.Users.Add(
            new User
            {
                Id = subjectUserId,
                TwitchUserId = "tw-resolve",
                Username = "resolve",
                UsernameNormalized = "resolve",
                DisplayName = "Resolve",
                SubjectKeyId = fkKey.Id,
            }
        );

        // A DEK sealing the subject's slice of a multi-subject journal event (O.1a mapping).
        CryptoKey eventKey = new()
        {
            KeyScope = "subject",
            SubjectIdHash = "gift-slice-hash",
            WrappedKeyMaterial = "wrapped",
            Provider = "local_aes",
            Algorithm = "AES-256-GCM",
            Status = "active",
        };
        db.CryptoKeys.Add(eventKey);
        db.EventSubjectKeys.Add(
            new EventSubjectKey
            {
                EventId = Guid.CreateVersion7(),
                SubjectIdHash = subjectIdHash,
                SubjectKeyId = eventKey.Id,
                Role = "recipient",
            }
        );

        // An already-destroyed generation under the hash needs no shred and stays out of the plan.
        CryptoKey deadKey = new()
        {
            KeyScope = "subject",
            SubjectIdHash = subjectIdHash,
            WrappedKeyMaterial = null,
            Provider = "local_aes",
            Algorithm = "AES-256-GCM",
            Status = "destroyed",
        };
        db.CryptoKeys.Add(deadKey);
        await db.SaveChangesAsync();

        Result<IReadOnlyList<Guid>> resolved = await service.ResolveSubjectKeysAsync(
            subjectUserId,
            subjectIdHash
        );

        resolved.IsSuccess.Should().BeTrue(resolved.ErrorMessage);
        resolved
            .Value.Should()
            .BeEquivalentTo(
                [firstKey, successorKey, fkKey.Id, eventKey.Id],
                "the shred set covers every generation, the FK'd key, and the event-mapped slice — "
                    + "and never an already-destroyed key"
            );
    }
}
