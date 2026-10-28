// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Per-tenant monotonic sequence allocator (schema Q.3). Read-and-increments a <c>TenantSequences</c> row for
/// <c>(BroadcasterId, SequenceName)</c> UNDER A ROW LOCK (<c>SELECT … FOR UPDATE</c> on Postgres;
/// <c>BEGIN IMMEDIATE</c> write-lock on SQLite), in the caller's AMBIENT transaction, so the allocation commits
/// atomically with the consuming insert. Creates the row at <c>NextValue=1</c> if absent.
/// </summary>
public interface ITenantSequenceAllocator
{
    /// <summary>The sequence name this subsystem owns for the journal's per-tenant <c>StreamPosition</c>.</summary>
    public const string EventStreamPositionSequence = "event_stream_position";

    /// <summary>
    /// Hands out the next value for <c>(broadcasterId, sequenceName)</c>. MUST be called inside an open
    /// transaction. Returns the value handed out.
    /// </summary>
    Task<Result<long>> NextAsync(
        Guid broadcasterId,
        string sequenceName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reserves a contiguous block of <paramref name="count"/> values in one increment (batch append).
    /// Returns the FIRST value; the caller assigns <c>first..first+count-1</c>. Same locking/transaction rules
    /// as <see cref="NextAsync"/>.
    /// </summary>
    Task<Result<long>> NextBlockAsync(
        Guid broadcasterId,
        string sequenceName,
        int count,
        CancellationToken cancellationToken = default
    );
}
