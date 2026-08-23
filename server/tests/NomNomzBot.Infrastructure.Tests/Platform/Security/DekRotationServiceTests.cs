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
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// Proves the KEK-rotation re-wrap pass (S098e): DEKs sealed under a previous root key are re-wrapped under
/// the current one, decrypt correctly afterward, the pass is idempotent, and an orphaned DEK is reported as
/// a named failure without aborting the others.
/// </summary>
public sealed class DekRotationServiceTests
{
    // Two distinct 32-byte base64 deployment keys — the previous and current root key across a rotation.
    private const string KeyA = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";
    private const string KeyB = "QmFyLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";

    private static CipherAad Aad(int index) => new($"tenant-{index}", "twitch", "access", "1");

    private static (
        ISubjectKeyService ServiceUnderKeyA,
        ISubjectKeyStore Store,
        IDekRotationService Rotation
    ) Build(string databaseName)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(db);

        IKeyVault vaultA = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = KeyA }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        ISubjectKeyService serviceA = new SubjectKeyService(
            vaultA,
            new AesGcmFieldCipher(),
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );

        IDekRotationService rotation = new DekRotationService(
            store,
            NullLogger<DekRotationService>.Instance,
            NullLoggerFactory.Instance
        );

        return (serviceA, store, rotation);
    }

    private static ISubjectKeyService ServiceUnderKeyB(string databaseName)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(db);
        IKeyVault vaultB = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = KeyB }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        return new SubjectKeyService(
            vaultB,
            new AesGcmFieldCipher(),
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
    }

    [Fact]
    public async Task RotateAllDeks_RewrapsEverySealedDek_AndAllDecryptUnderTheNewKey()
    {
        string dbName = Guid.NewGuid().ToString();
        (ISubjectKeyService serviceA, _, IDekRotationService rotation) = Build(dbName);

        const int subjectCount = 5;
        List<(Guid KeyId, CipherPayload Payload, string Plaintext)> sealed_ = [];
        for (int i = 0; i < subjectCount; i++)
        {
            Result<Guid> keyId = await serviceA.GetOrCreateSubjectKeyAsync(
                Guid.CreateVersion7(),
                subjectIdHash: Guid.NewGuid().ToString("N"),
                CancellationToken.None
            );
            keyId.IsSuccess.Should().BeTrue();

            string plaintext = $"secret-{i}";
            Result<CipherPayload> payload = await serviceA.ProtectAsync(
                keyId.Value,
                plaintext,
                Aad(i),
                "IntegrationTokens",
                "CipherText",
                CancellationToken.None
            );
            payload.IsSuccess.Should().BeTrue();
            sealed_.Add((keyId.Value, payload.Value, plaintext));
        }

        Result<DekRotationSummary> result = await rotation.RotateAllDeksAsync(
            KeyA,
            KeyB,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.RewrappedCount.Should().Be(subjectCount);
        result.Value.Failures.Should().BeEmpty();

        ISubjectKeyService serviceB = ServiceUnderKeyB(dbName);
        for (int i = 0; i < sealed_.Count; i++)
        {
            (Guid keyId, CipherPayload payload, string plaintext) = sealed_[i];
            Result<string> opened = await serviceB.UnprotectAsync(
                keyId,
                payload,
                Aad(i),
                CancellationToken.None
            );
            opened.IsSuccess.Should().BeTrue();
            opened.Value.Should().NotBeNullOrWhiteSpace();
            opened.Value.Should().Be(plaintext);
        }
    }

    [Fact]
    public async Task RotateAllDeks_SecondRun_ReportsZero_AndSecretsStillDecrypt()
    {
        string dbName = Guid.NewGuid().ToString();
        (ISubjectKeyService serviceA, _, IDekRotationService rotation) = Build(dbName);

        Result<Guid> keyId = await serviceA.GetOrCreateSubjectKeyAsync(
            Guid.CreateVersion7(),
            subjectIdHash: Guid.NewGuid().ToString("N"),
            CancellationToken.None
        );
        Result<CipherPayload> payload = await serviceA.ProtectAsync(
            keyId.Value,
            "secret",
            Aad(0),
            "IntegrationTokens",
            "CipherText",
            CancellationToken.None
        );

        Result<DekRotationSummary> first = await rotation.RotateAllDeksAsync(
            KeyA,
            KeyB,
            CancellationToken.None
        );
        first.Value.RewrappedCount.Should().Be(1);

        Result<DekRotationSummary> second = await rotation.RotateAllDeksAsync(
            KeyA,
            KeyB,
            CancellationToken.None
        );
        second.IsSuccess.Should().BeTrue();
        second.Value.RewrappedCount.Should().Be(0);
        second.Value.AlreadyCurrentCount.Should().Be(1);
        second.Value.Failures.Should().BeEmpty();

        ISubjectKeyService serviceB = ServiceUnderKeyB(dbName);
        Result<string> opened = await serviceB.UnprotectAsync(
            keyId.Value,
            payload.Value,
            Aad(0),
            CancellationToken.None
        );
        opened.IsSuccess.Should().BeTrue();
        opened.Value.Should().Be("secret");
    }

    [Fact]
    public async Task RotateAllDeks_OrphanedDek_ReportsFailure_AndDoesNotAbortTheOthers()
    {
        string dbName = Guid.NewGuid().ToString();
        (ISubjectKeyService serviceA, ISubjectKeyStore store, IDekRotationService rotation) = Build(
            dbName
        );

        // A healthy DEK sealed under KeyA.
        Result<Guid> healthyKeyId = await serviceA.GetOrCreateSubjectKeyAsync(
            Guid.CreateVersion7(),
            subjectIdHash: Guid.NewGuid().ToString("N"),
            CancellationToken.None
        );
        await serviceA.ProtectAsync(
            healthyKeyId.Value,
            "secret",
            Aad(0),
            "IntegrationTokens",
            "CipherText",
            CancellationToken.None
        );

        // An orphaned DEK: wrapped material that unwraps under neither KeyA nor KeyB.
        Result<Guid> orphanKeyId = await serviceA.GetOrCreateSubjectKeyAsync(
            Guid.CreateVersion7(),
            subjectIdHash: Guid.NewGuid().ToString("N"),
            CancellationToken.None
        );
        SubjectKeyRecord? orphan = await store.GetAsync(orphanKeyId.Value, CancellationToken.None);
        orphan.Should().NotBeNull();
        await store.UpdateAsync(
            orphan! with
            {
                WrappedKeyMaterial = Convert.ToBase64String(new byte[60]), // well-formed length, garbage tag
            },
            CancellationToken.None
        );

        Result<DekRotationSummary> result = await rotation.RotateAllDeksAsync(
            KeyA,
            KeyB,
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.RewrappedCount.Should().Be(1);
        result.Value.Failures.Should().ContainSingle(f => f.CryptoKeyId == orphanKeyId.Value);
    }
}
