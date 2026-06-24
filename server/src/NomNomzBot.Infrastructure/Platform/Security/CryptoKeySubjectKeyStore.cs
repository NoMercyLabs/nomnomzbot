// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// The persisted DEK registry backing <see cref="ISubjectKeyStore"/> over the <see cref="CryptoKey"/> table
/// (schema Q.1). Wrapped DEK material lives in the database alongside the ciphertext it opens and the FK
/// references to it (<c>IntegrationToken.EncryptionKeyId</c>, …), so a DEK minted in one process is found and
/// unwrapped in the next — the envelope survives a restart. Holds only wrapped material, never a plaintext DEK.
///
/// Scoped (it owns a <see cref="IApplicationDbContext"/>); writes commit immediately because a freshly-minted DEK
/// must be durable before its first ciphertext is sealed under it (otherwise a crash between mint and the caller's
/// own save would orphan unreadable ciphertext).
/// </summary>
public sealed class CryptoKeySubjectKeyStore : ISubjectKeyStore
{
    private readonly IApplicationDbContext _db;

    public CryptoKeySubjectKeyStore(IApplicationDbContext db) => _db = db;

    public async Task<SubjectKeyRecord?> GetAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken = default
    )
    {
        CryptoKey? entity = await _db
            .CryptoKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.Id == cryptoKeyId, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<SubjectKeyRecord?> GetActiveByIdentityAsync(
        string keyScope,
        Guid? broadcasterId,
        string? subjectIdHash,
        CancellationToken cancellationToken = default
    )
    {
        CryptoKey? entity = await _db
            .CryptoKeys.AsNoTracking()
            .FirstOrDefaultAsync(
                k =>
                    k.Status == ActiveStatus
                    && k.KeyScope == keyScope
                    && k.BroadcasterId == broadcasterId
                    && k.SubjectIdHash == subjectIdHash,
                cancellationToken
            );
        return entity is null ? null : ToRecord(entity);
    }

    public async Task AddAsync(
        SubjectKeyRecord record,
        CancellationToken cancellationToken = default
    )
    {
        _db.CryptoKeys.Add(ToEntity(record));
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        SubjectKeyRecord record,
        CancellationToken cancellationToken = default
    )
    {
        CryptoKey? entity = await _db.CryptoKeys.FirstOrDefaultAsync(
            k => k.Id == record.Id,
            cancellationToken
        );
        if (entity is null)
        {
            // The record was minted in a prior process and is not tracked here — attach the projected state.
            _db.CryptoKeys.Add(ToEntity(record));
        }
        else
        {
            ApplyTo(entity, record);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private const string ActiveStatus = "active";

    private static SubjectKeyRecord ToRecord(CryptoKey e) =>
        new()
        {
            Id = e.Id,
            KeyScope = e.KeyScope,
            BroadcasterId = e.BroadcasterId,
            SubjectIdHash = e.SubjectIdHash,
            WrappedKeyMaterial = e.WrappedKeyMaterial,
            KekReference = e.KekReference,
            Provider = e.Provider,
            Algorithm = e.Algorithm,
            KeyVersion = e.KeyVersion.ToString(CultureInfo.InvariantCulture),
            Status = ParseStatus(e.Status),
            DestroyedAt = e.DestroyedAt,
            ErasureRequestId = e.ErasureRequestId,
        };

    private static CryptoKey ToEntity(SubjectKeyRecord r)
    {
        CryptoKey entity = new()
        {
            Id = r.Id,
            KeyScope = r.KeyScope,
            BroadcasterId = r.BroadcasterId,
            SubjectIdHash = r.SubjectIdHash,
            KeyVersion = ParseKeyVersion(r.KeyVersion),
        };
        ApplyTo(entity, r);
        return entity;
    }

    private static void ApplyTo(CryptoKey entity, SubjectKeyRecord r)
    {
        entity.WrappedKeyMaterial = r.WrappedKeyMaterial;
        entity.KekReference = r.KekReference;
        entity.Provider = r.Provider;
        entity.Algorithm = r.Algorithm;
        entity.KeyVersion = ParseKeyVersion(r.KeyVersion);
        entity.Status = ToStatusString(r.Status);
        entity.DestroyedAt = r.DestroyedAt;
        entity.ErasureRequestId = r.ErasureRequestId;
    }

    private static string ToStatusString(SubjectKeyStatus status) =>
        status switch
        {
            SubjectKeyStatus.Active => "active",
            SubjectKeyStatus.Rotating => "rotating",
            SubjectKeyStatus.Destroyed => "destroyed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown key status."
            ),
        };

    private static SubjectKeyStatus ParseStatus(string status) =>
        status switch
        {
            "active" => SubjectKeyStatus.Active,
            "rotating" => SubjectKeyStatus.Rotating,
            "destroyed" => SubjectKeyStatus.Destroyed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unknown key status."
            ),
        };

    private static int ParseKeyVersion(string keyVersion) =>
        int.TryParse(keyVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : 1;
}
