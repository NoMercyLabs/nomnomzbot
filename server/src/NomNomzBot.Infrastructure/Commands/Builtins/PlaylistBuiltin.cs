// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!playlist</c> (legacy parity, S068d) — a summary of the current music queue: the playing track plus
/// how many are lined up behind it, read live from <see cref="IMusicService.GetQueueAsync"/> — the same
/// queue read <c>!queue</c> (<see cref="QueueBuiltin"/>) uses. No provider surface returns a shareable
/// queue/playlist URL (<see cref="IMusicService"/> has no such member), so this stays a chat summary rather
/// than inventing a link.
/// </summary>
public sealed class PlaylistBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    : IBuiltinCommand
{
    public string BuiltinKey => "playlist";
    public int DefaultCooldownSeconds => 10;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        MusicQueue queue = await music.GetQueueAsync(context.BroadcasterId.ToString(), ct);

        bool nothingPlaying = queue.CurrentTrack is null;
        bool queueEmpty = queue.Queue.Count == 0;

        if (nothingPlaying && queueEmpty)
        {
            string empty = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = "empty",
                    NeutralFallback = "Nothing is playing and the queue is empty.",
                },
                ct
            );
            return Result.Success(empty);
        }

        string nowPlaying = queue.CurrentTrack is { } current
            ? $"{current.TrackName} by {current.Artist}"
            : "nothing right now";

        string upcoming = queueEmpty
            ? "nothing queued after this"
            : string.Join(", ", queue.Queue.Take(5).Select(t => $"{t.TrackName} by {t.Artist}"));

        string message = await composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = "summary",
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback =
                    "Now playing: {playlist.nowplaying} — up next: {playlist.upcoming} ({playlist.count} queued)",
                Variables = new Dictionary<string, string>
                {
                    ["playlist.nowplaying"] = nowPlaying,
                    ["playlist.upcoming"] = upcoming,
                    ["playlist.count"] = queue.Queue.Count.ToString(),
                },
            },
            ct
        );
        return Result.Success(message);
    }
}
