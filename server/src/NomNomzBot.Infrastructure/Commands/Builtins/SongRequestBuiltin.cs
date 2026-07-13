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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// !sr &lt;query&gt; — requests a song to be added to the queue. Delegates to IMusicService for search and
/// queue management, then phrases the added / not-found outcome in the channel's personality tone. Pure
/// usage and "could not add" errors stay neutral (functional).
/// </summary>
public sealed class SongRequestBuiltin : IBuiltinCommand
{
    private readonly IMusicService _music;
    private readonly IBuiltinResponseComposer _composer;

    public SongRequestBuiltin(IMusicService music, IBuiltinResponseComposer composer)
    {
        _music = music;
        _composer = composer;
    }

    public string BuiltinKey => BuiltinResponseSlots.SongRequest.Key;
    public int DefaultCooldownSeconds => 5;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string query = context.Args.Trim();
        if (string.IsNullOrWhiteSpace(query))
            // Pure usage string — functional, never personality. Sent as a reply, so no "@user" prefix.
            return Result.Success("Usage: !sr <song name or URL>");

        IReadOnlyList<MusicTrack> results = await _music.SearchAsync(
            context.BroadcasterId.ToString(),
            query,
            1,
            ct
        );

        if (results.Count == 0)
        {
            string notFound = await _composer.ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinKey,
                    Slot = BuiltinResponseSlots.SongRequest.NotFound,
                    NeutralFallback = "No tracks found for \"{query}\".",
                    Variables = new Dictionary<string, string>
                    {
                        ["user"] = context.TriggeringUserDisplayName,
                        ["query"] = query,
                    },
                },
                ct
            );
            return Result.Success(notFound);
        }

        MusicTrack track = results[0];
        bool added = await _music.AddToQueueAsync(
            context.BroadcasterId.ToString(),
            track.Uri,
            context.TriggeringUserDisplayName,
            ct
        );

        if (!added)
            // Functional failure — stays neutral. Sent as a reply, so no "@user" prefix.
            return Result.Success($"Could not add \"{track.Name}\" to the queue.");

        string message = await _composer.ComposeAsync(
            new BuiltinResponseRequest
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinKey,
                Slot = BuiltinResponseSlots.SongRequest.Added,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback = "Added {track.name} by {track.artist} to the queue.",
                Variables = new Dictionary<string, string>
                {
                    ["user"] = context.TriggeringUserDisplayName,
                    ["track.name"] = track.Name,
                    ["track.artist"] = track.Artist,
                },
            },
            ct
        );
        return Result.Success(message);
    }
}
