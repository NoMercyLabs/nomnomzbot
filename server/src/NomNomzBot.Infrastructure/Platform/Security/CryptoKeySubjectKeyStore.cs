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
using NomNomzBot.Domain.EventStore.Entities;
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

    public async Task EnsureUsageBindingAsync(
        Guid cryptoKeyId,
        Guid? broadcasterId,
        string resourceTable,
        string resourceColumn,
        CancellationToken cancellationToken = default
    )
    {
        bool exists = await _db.KeyUsageBindings.AnyAsync(
            b =>
                b.CryptoKeyId == cryptoKeyId
                && b.ResourceTable == resourceTable
                && b.ResourceColumn == resourceColumn,
            cancellationToken
        );
        if (exists)
            return;

        _db.KeyUsageBindings.Add(
            new KeyUsageBinding
            {
                CryptoKeyId = cryptoKeyId,
                BroadcasterId = broadcasterId,
                ResourceTable = resourceTable,
                ResourceColumn = resourceColumn,
            }
        );
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRotationAsync(
        SubjectKeyRecord successor,
        SubjectKeyRecord retiredPredecessor,
        CancellationToken cancellationToken = default
    )
    {
        // One SaveChanges = one implicit transaction: the successor, the retirement, and the carried-over
        // bindings land together or not at all — the identity never persists with two active generations.
        _db.CryptoKeys.Add(ToEntity(successor));

        CryptoKey? predecessor = await _db.CryptoKeys.FirstOrDefaultAsync(
            k => k.Id == retiredPredecessor.Id,
            cancellationToken
        );
        if (predecessor is null)
            _db.CryptoKeys.Add(ToEntity(retiredPredecessor));
        else
            ApplyTo(predecessor, retiredPredecessor);

        List<KeyUsageBinding> bindings = await _db
            .KeyUsageBindings.AsNoTracking()
            .Where(b => b.CryptoKeyId == retiredPredecessor.Id)
            .ToListAsync(cancellationToken);
        foreach (KeyUsageBinding binding in bindings)
        {
            _db.KeyUsageBindings.Add(
                new KeyUsageBinding
                {
                    CryptoKeyId = successor.Id,
                    BroadcasterId = binding.BroadcasterId,
                    ResourceTable = binding.ResourceTable,
                    ResourceColumn = binding.ResourceColumn,
                }
            );
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ResolveSubjectKeyIdsAsync(
        Guid subjectUserId,
        string subjectIdHash,
        CancellationToken cancellationToken = default
    )
    {
        // Candidate ids beyond the hash sweep: the Users FK and every event-mapped DEK (O.1a slices).
        List<Guid> candidates = await _db
            .Users.Where(u => u.Id == subjectUserId && u.SubjectKeyId != null)
            .Select(u => u.SubjectKeyId!.Value)
            .ToListAsync(cancellationToken);
        candidates.AddRange(
            await _db
                .EventSubjectKeys.Where(e => e.SubjectIdHash == subjectIdHash)
                .Select(e => e.SubjectKeyId)
                .ToListAsync(cancellationToken)
        );

        // One registry query filters everything to non-destroyed keys: the hash sweep picks up every
        // subject-scope generation (rotated predecessors share the identity), the candidate list folds in
        // the FK'd + event-mapped keys. An already-destroyed key needs no shred and is excluded.
        List<Guid> resolved = await _db
            .CryptoKeys.Where(k =>
                k.Status != DestroyedStatus
                && (
                    (k.KeyScope == SubjectScope && k.SubjectIdHash == subjectIdHash)
                    || candidates.Contains(k.Id)
                )
            )
            .Select(k => k.Id)
            .ToListAsync(cancellationToken);
        return resolved.Distinct().ToList();
    }

    private const string ActiveStatus = "active";
    private const string DestroyedStatus = "destroyed";
    private const string SubjectScope = "subject";

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
            RotatedFromKeyId = e.RotatedFromKeyId,
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
        entity.RotatedFromKeyId = r.RotatedFromKeyId;
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
