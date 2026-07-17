// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// The per-channel song-request queue changed — a request was added, dequeued into playback on skip, or
/// removed. Carries the fresh top-of-queue snapshot (in play order, capped by the publisher) so standing
/// overlay surfaces (the <c>sr_queue</c> widget) re-render the upcoming list from the event alone,
/// without re-reading the fair queue (music-sr.md).
/// </summary>
public sealed class SongRequestQueueChangedEvent : DomainEventBase
{
    /// <summary>The upcoming requests, top of the fair queue first.</summary>
    public required IReadOnlyList<SongRequestQueueSnapshotItem> Items { get; init; }
}

/// <summary>One upcoming request in the snapshot — exactly the fields the sr_queue overlay renders.</summary>
public sealed record SongRequestQueueSnapshotItem(
    string Title,
    string RequestedBy,
    int DurationSec
);
