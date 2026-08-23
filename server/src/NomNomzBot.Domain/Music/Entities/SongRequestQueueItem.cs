// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Domain.Music.Entities;

/// <summary>
/// A durable mirror of one entry currently sitting in a channel's in-memory song-request
/// <c>FairQueue&lt;SongRequestEntry&gt;</c> (<c>SongRequestQueueStore</c>, S001b). The in-memory queue
/// stays the single source of truth for serving traffic; this table exists only so a bot restart —
/// graceful or a hard kill — can rebuild that queue instead of silently dropping every pending
/// request. <see cref="Sequence"/> is the channel-scoped insertion order the fair-queue rank algorithm
/// is a deterministic function of: replaying rows in ascending <see cref="Sequence"/> through
/// <c>FairQueue.Enqueue</c> on restore reproduces the exact same rank/order state the queue had before
/// the restart. Rows are kept in lock-step with the live queue (inserted on enqueue, deleted on
/// dequeue/removal) rather than modeling an audit trail — there is no soft delete here, a removed
/// queue entry has nothing left to keep a record of.
/// </summary>
public class SongRequestQueueItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning channel (tenant key) — the same raw broadcaster-id string the in-memory
    /// store is keyed by.</summary>
    [MaxLength(50)]
    public string BroadcasterId { get; set; } = null!;

    /// <summary>Monotonic per-<see cref="BroadcasterId"/> insertion order; the sole ordering key on restore.</summary>
    public long Sequence { get; set; }

    /// <summary>The fair-queue owner key (the requester) — matches <see cref="SongRequestEntry.RequestedBy"/>.</summary>
    [MaxLength(100)]
    public string OwnerKey { get; set; } = null!;

    [MaxLength(500)]
    public string TrackUri { get; set; } = null!;

    [MaxLength(200)]
    public string TrackName { get; set; } = null!;

    [MaxLength(200)]
    public string Artist { get; set; } = null!;

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    public int DurationMs { get; set; }

    /// <summary>When this row was written — the freshness clock <c>SongRequestQueueRestoreService</c>
    /// checks a channel's oldest row against before trusting a restore.</summary>
    public DateTime CreatedAt { get; set; }
}
