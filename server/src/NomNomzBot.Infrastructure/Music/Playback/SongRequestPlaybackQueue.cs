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

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// <see cref="IPlaybackQueue"/> over the live song request fair queue. A row is identified by its
/// short code, or by track and requester for an old row without one. It removes one row at a time
/// and keeps what it removed, so a director can put the same entry back at the head.
/// </summary>
public sealed class SongRequestPlaybackQueue(FairQueue<SongRequestEntry> queue) : IPlaybackQueue
{
    private readonly Dictionary<string, SongRequestEntry> _removed = [];

    public PlaybackItem? Head => Snapshot.FirstOrDefault();

    public IReadOnlyList<PlaybackItem> Snapshot =>
        [.. queue.GetSnapshot().Select(e => ToItem(e.Item, e.OwnerKey))];

    public bool Remove(string itemId)
    {
        SongRequestEntry? entry = queue
            .GetSnapshot()
            .Select(e => e.Item)
            .FirstOrDefault(e => IdOf(e) == itemId);
        if (entry is null)
            return false;

        bool isHead = ReferenceEquals(queue.Peek(), entry);
        bool removed = isHead
            ? queue.Dequeue() is not null
            : queue.RemoveFirst(e => ReferenceEquals(e, entry));
        if (removed)
            _removed[itemId] = entry;

        return removed;
    }

    public void RequeueAtHead(PlaybackItem item)
    {
        SongRequestEntry entry = _removed.GetValueOrDefault(item.Id) ?? ToEntry(item);
        queue.Enqueue(item.OwnerKey, entry);
        queue.MoveToFront(queue.Count - 1);
    }

    private static string IdOf(SongRequestEntry entry) =>
        entry.Code.Length > 0 ? entry.Code : $"{entry.TrackUri}|{entry.RequestedBy}";

    private static PlaybackItem ToItem(SongRequestEntry entry, string ownerKey) =>
        new(
            IdOf(entry),
            ownerKey,
            EngineKinds.ForTrack(null, entry.TrackUri),
            entry.TrackUri,
            entry.TrackName,
            [entry.Artist],
            entry.ImageUrl,
            entry.DurationMs,
            entry.RequestedBy
        );

    private static SongRequestEntry ToEntry(PlaybackItem item) =>
        new(
            item.Uri,
            item.Title,
            item.Artists.FirstOrDefault() ?? "",
            item.AlbumArtUrl,
            item.DurationMs,
            item.RequestedBy ?? "",
            0,
            null,
            item.Id
        );
}
