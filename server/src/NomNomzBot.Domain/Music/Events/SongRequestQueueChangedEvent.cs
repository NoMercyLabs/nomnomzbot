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
/// <param name="Title">The song title.</param>
/// <param name="RequestedBy">The display name of the viewer who requested the song.</param>
/// <param name="DurationSec">The song length in seconds.</param>
/// <param name="Number">The number viewers see for this entry: the same one <c>!queue</c> prints and <c>!wrongsong N</c> reads back (1 plays next).</param>
/// <param name="Code">The short handle a viewer says to name this request, for example K7QM. Empty only for a snapshot made before codes existed.</param>
public sealed record SongRequestQueueSnapshotItem(
    string Title,
    string RequestedBy,
    int DurationSec,
    int Number,
    // The short speakable handle a viewer uses to name THIS request (Domain SongCode) — e.g. "K7QM" —
    // so the overlay can show it and a viewer can read it aloud for !wrongsong. Deliberately NO default:
    // a positional default here is exactly what let two production publishers construct this record
    // without ever passing the real code, silently shipping an empty badge (S-MUSIC-4c). Every call
    // site must now name a code explicitly — pass "" only for a snapshot built before codes existed.
    string Code
);
