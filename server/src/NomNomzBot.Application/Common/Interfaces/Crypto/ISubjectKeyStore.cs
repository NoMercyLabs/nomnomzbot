// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models.Crypto;

namespace NomNomzBot.Application.Common.Interfaces.Crypto;

/// <summary>
/// Persistence port for the DEK registry (<c>CryptoKey</c>, schema Q.1). Isolates <c>ISubjectKeyService</c>
/// from the storage backend so the security-critical crypto is identical regardless of where the wrapped
/// DEKs live. The erasure subsystem ships the EF / <c>CryptoKey</c>-table-backed implementation; until that
/// table and the <c>Guid BroadcasterId</c> widening land (schema build-dependency, gdpr-crypto §10), an
/// in-process store backs the same contract so the crypto core is complete and testable now.
///
/// Implementations must enforce the registry invariant: at most one <see cref="SubjectKeyStatus.Active"/>
/// record per <c>(KeyScope, BroadcasterId, SubjectIdHash)</c> identity.
/// </summary>
public interface ISubjectKeyStore
{
    /// <summary>Loads a record by its DEK id, or null if no such key exists.</summary>
    Task<SubjectKeyRecord?> GetAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the single active record for the identity, or null if none is active (never created, or
    /// the prior key was crypto-shredded — in which case a fresh DEK must be minted, not resurrected).
    /// </summary>
    Task<SubjectKeyRecord?> GetActiveByIdentityAsync(
        string keyScope,
        Guid? broadcasterId,
        string? subjectIdHash,
        CancellationToken cancellationToken = default
    );

    /// <summary>Inserts a new active record.</summary>
    Task AddAsync(SubjectKeyRecord record, CancellationToken cancellationToken = default);

    /// <summary>Replaces an existing record (matched by <see cref="SubjectKeyRecord.Id"/>).</summary>
    Task UpdateAsync(SubjectKeyRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asserts a <c>KeyUsageBinding</c> (schema Q.2) for the triple — records which table/column stores
    /// ciphertext under the DEK. Idempotent: an existing binding for the same triple is left as-is.
    /// </summary>
    Task EnsureUsageBindingAsync(
        Guid cryptoKeyId,
        Guid? broadcasterId,
        string resourceTable,
        string resourceColumn,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Persists a rotation atomically (one save): inserts the <paramref name="successor"/>, replaces the
    /// <paramref name="retiredPredecessor"/> (its <c>rotating</c> status retiring it from writes while its
    /// wrapped material stays readable), and copies the predecessor's usage bindings to the successor so the
    /// inventory names which resources the new generation covers going forward.
    /// </summary>
    Task AddRotationAsync(
        SubjectKeyRecord successor,
        SubjectKeyRecord retiredPredecessor,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Read-only shred planning (gdpr-crypto §3.4): every non-destroyed DEK id belonging to the subject —
    /// the <c>Users.SubjectKeyId</c> FK, every <c>subject</c>-scope generation registered under the hash
    /// (rotated predecessors included), and every DEK mapped to the hash via <c>EventSubjectKeys</c> (O.1a).
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveSubjectKeyIdsAsync(
        Guid subjectUserId,
        string subjectIdHash,
        CancellationToken cancellationToken = default
    );
}
