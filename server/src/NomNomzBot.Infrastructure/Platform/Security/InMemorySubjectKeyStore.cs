// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models.Crypto;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// In-process DEK registry backing <see cref="ISubjectKeyStore"/>. Holds only wrapped DEK material (never a
/// plaintext DEK), exactly like the future <c>CryptoKey</c> table. This is the single deferred seam: when the
/// <c>CryptoKey</c> EF entity and the <c>Guid BroadcasterId</c> widening land (schema build-dependency,
/// gdpr-crypto §10), an EF-backed store replaces this registration with zero change to the crypto core.
/// Registered as a singleton so DEK records survive for the process lifetime.
/// </summary>
public sealed class InMemorySubjectKeyStore : ISubjectKeyStore
{
    private readonly ConcurrentDictionary<Guid, SubjectKeyRecord> _records = new();

    public Task<SubjectKeyRecord?> GetAsync(
        Guid cryptoKeyId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_records.GetValueOrDefault(cryptoKeyId));

    public Task<SubjectKeyRecord?> GetActiveByIdentityAsync(
        string keyScope,
        Guid? broadcasterId,
        string? subjectIdHash,
        CancellationToken cancellationToken = default
    )
    {
        SubjectKeyRecord? match = _records.Values.FirstOrDefault(r =>
            r.Status == SubjectKeyStatus.Active
            && r.KeyScope == keyScope
            && r.BroadcasterId == broadcasterId
            && r.SubjectIdHash == subjectIdHash
        );
        return Task.FromResult(match);
    }

    public Task AddAsync(SubjectKeyRecord record, CancellationToken cancellationToken = default)
    {
        if (!_records.TryAdd(record.Id, record))
            throw new InvalidOperationException($"Key {record.Id} already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SubjectKeyRecord record, CancellationToken cancellationToken = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }
}
