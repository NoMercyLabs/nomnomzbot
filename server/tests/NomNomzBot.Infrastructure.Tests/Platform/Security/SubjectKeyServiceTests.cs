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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// End-to-end envelope-encryption tests over the real stack: AES-256-GCM field cipher + OS-store key vault
/// (deterministic config-KEK path) + in-process DEK registry. Proves the four mandatory security behaviors
/// through the full DEK-wrapped-by-KEK envelope, including the crypto-shred GDPR guarantee.
/// </summary>
public class SubjectKeyServiceTests
{
    // A fixed base64 32-byte deployment key drives the deterministic KEK fallback (no OS keystore needed).
    private const string ConfigKey = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh"; // 32 bytes, base64

    private static (ISubjectKeyService Service, ISubjectKeyStore Store) Build()
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = ConfigKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        ISubjectKeyStore store = new InMemorySubjectKeyStore();
        ISubjectKeyService service = new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
        return (service, store);
    }

    private static CipherAad Aad(string tenant = "tenant-A") =>
        new(tenant, "twitch", "access", "1");

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

        Result<CipherPayload> sealed_ = await service.ProtectAsync(keyId, secret, Aad());
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
            Aad("tenant-A")
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
            Aad()
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
        Result<CipherPayload> sealed_ = await service.ProtectAsync(keyId, "value", Aad());

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
        Result<CipherPayload> sealed_ = await service.ProtectAsync(keyId, "to-be-shredded", Aad());

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
}
