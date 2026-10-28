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

namespace NomNomzBot.Domain.EventStore.Entities;

/// <summary>
/// App-assigned per-tenant monotonic counter (schema Q.3). DB auto-increment is global (and SQLite has no
/// sequences), so monotonic-per-tenant values such as <c>EventJournal.StreamPosition</c> are computed here:
/// the allocator read-increments <see cref="NextValue"/> for <c>(BroadcasterId, SequenceName)</c> under a row
/// lock in the same transaction as the consuming insert. Carries <see cref="UpdatedAt"/> (not append-only),
/// but is a standalone keyed entity — it does not inherit <c>BaseEntity</c>.
/// </summary>
public class TenantSequence
{
    /// <summary>Surrogate key (UUIDv7).</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning tenant.</summary>
    public Guid BroadcasterId { get; set; }

    /// <summary>Counter name, e.g. <c>event_stream_position</c>. Unique with <see cref="BroadcasterId"/>.</summary>
    public string SequenceName { get; set; } = null!;

    /// <summary>Next value to hand out; incremented in the same txn as the consuming insert.</summary>
    public long NextValue { get; set; }

    /// <summary>Last allocation time.</summary>
    public DateTime UpdatedAt { get; set; }
}
