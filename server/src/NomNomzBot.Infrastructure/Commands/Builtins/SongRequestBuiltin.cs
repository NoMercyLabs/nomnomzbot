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
/// !sr &lt;query&gt; — requests a song to be added to the queue.
/// Delegates to IMusicService for search and queue management.
/// </summary>
public sealed class SongRequestBuiltin : IBuiltinCommand
{
    private readonly IMusicService _music;

    public SongRequestBuiltin(IMusicService music)
    {
        _music = music;
    }

    public string BuiltinKey => "sr";
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string query = context.Args.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} Usage: !sr <song name or URL>"
            );

        IReadOnlyList<MusicTrack> results = await _music.SearchAsync(
            context.BroadcasterId.ToString(),
            query,
            1,
            ct
        );

        if (results.Count == 0)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} No tracks found for \"{query}\"."
            );

        MusicTrack track = results[0];
        bool added = await _music.AddToQueueAsync(
            context.BroadcasterId.ToString(),
            track.Uri,
            context.TriggeringUserDisplayName,
            ct
        );

        if (!added)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} Could not add \"{track.Name}\" to the queue."
            );

        return Result.Success(
            $"@{context.TriggeringUserDisplayName} Added: {track.Name} by {track.Artist}"
        );
    }
}
