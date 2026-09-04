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
using NomNomzBot.Domain.Music.ValueObjects;

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

    /// <summary>
    /// True for the single row (per <see cref="BroadcasterId"/>, at most one) that mirrors
    /// <c>SongRequestQueueStore.GetInFlight</c> — the request already handed to the provider and
    /// awaited, not merely queued. Without this a restart forgets which entry was already handed over:
    /// <c>SongRequestQueueReconciler</c> sees an empty in-flight slot and hands the same head entry to
    /// the provider a second time, replaying the track that was already playing (the "same songs over
    /// and over" loop seen live on 2026-08-25, multiplied by every crash-restart). The row itself is
    /// never removed while in-flight — the provider hand-off does not dequeue — so this is purely a flag
    /// on an otherwise-ordinary row, not a second table.
    /// </summary>
    public bool IsInFlight { get; set; }

    /// <summary>
    /// The currency amount debited from <see cref="RequesterUserId"/> to admit this request — 0 means
    /// free (S067b). No admission path charges for a song request today, so this is currently always 0;
    /// it exists so a moderator removal/ban can refund the debit once a paid-request mechanism is wired,
    /// without a further schema change.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>The viewer account to refund <see cref="Cost"/> to on removal — null whenever
    /// <see cref="Cost"/> is 0, or the requester could not be resolved to a viewer account (e.g. an
    /// anonymous public song-request page submission).</summary>
    public Guid? RequesterUserId { get; set; }

    /// <summary>
    /// Mirrors <see cref="NomNomzBot.Infrastructure.Music.SongRequestEntry.Code"/> — the short speakable
    /// handle (<see cref="SongCode"/>) a viewer names this request by, e.g. <c>!wrongsong K7QM</c>.
    /// Without this column a restart forgot every issued code: the in-memory queue rebuilt from this
    /// table came back with empty codes, so every code-addressed command silently stopped matching a
    /// restored entry. Default empty only for rows written before this column existed; every row written
    /// from now on carries a real code (backfilled for pre-existing rows by the migration that added
    /// this column).
    /// </summary>
    [MaxLength(SongCode.Length)]
    public string Code { get; set; } = string.Empty;
}
