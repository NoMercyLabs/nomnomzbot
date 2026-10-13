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
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>A viewer's song request that a limit refused, kept so a moderator can admit it with permission.</summary>
/// <param name="RequestedBy">The fair-queue owner key (the viewer's display name).</param>
/// <param name="RequesterUserId">The viewer's platform id, for their request history.</param>
/// <param name="Track">The resolved track, so admitting it never searches again.</param>
/// <param name="RefusedBy">The error code of the rule that refused it.</param>
internal sealed record RefusedSongRequest(
    string RequestedBy,
    string? RequesterUserId,
    TrackInfo Track,
    string RefusedBy
);

/// <summary>
/// Remembers, per channel and viewer, the last song request a limit refused (too long, queue full, per-viewer
/// cap) for <see cref="Lifetime"/>. One entry per viewer: a newer refusal replaces the older one, and taking
/// an entry removes it, so one <c>!permit</c> admits one request.
/// </summary>
public sealed class RefusedSongRequestStore
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<
        (string BroadcasterId, string Viewer),
        (RefusedSongRequest Request, DateTimeOffset ExpiresAt)
    > _held = new();
    private readonly TimeProvider _clock;

    public RefusedSongRequestStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    internal void Remember(string broadcasterId, RefusedSongRequest request) =>
        _held[Key(broadcasterId, request.RequestedBy)] = (request, _clock.GetUtcNow() + Lifetime);

    /// <summary>Removes and returns the viewer's held request; null when none is held or it expired.</summary>
    internal RefusedSongRequest? Take(string broadcasterId, string viewer)
    {
        if (
            !_held.TryRemove(
                Key(broadcasterId, viewer),
                out (RefusedSongRequest Request, DateTimeOffset ExpiresAt) held
            )
        )
            return null;

        return held.ExpiresAt > _clock.GetUtcNow() ? held.Request : null;
    }

    private static (string, string) Key(string broadcasterId, string viewer) =>
        (broadcasterId, viewer.Trim().ToLowerInvariant());
}
