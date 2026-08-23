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
using NomNomzBot.Domain.Identity.Enums;

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

        // One resolve: a pasted track link lands on its exact track, a search phrase falls through to the
        // provider's search — then straight into the fair queue (music-sr.md §3.9).
        Result<MusicTrack> requested = await _music.RequestTrackAsync(
            context.BroadcasterId.ToString(),
            query,
            context.TriggeringUserDisplayName,
            ct
        );

        if (requested.IsFailure)
        {
            if (requested.ErrorCode == "NOT_FOUND")
            {
                string notFound = await _composer.ComposeAsync(
                    new()
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

            // Functional failures — stay neutral. Sent as a reply, so no "@user" prefix. Each refusal
            // reason gets its own honest wording rather than one blanket "could not add": a blocked
            // track carries its typed reason straight through, and anything else (a genuinely erroring
            // provider — auth broken, API down) degrades to the same "try again" wording rather than a
            // confusing internal error code.
            return Result.Success(
                requested.ErrorCode switch
                {
                    "TRACK_BLOCKED" => requested.ErrorMessage!,
                    "SERVICE_UNAVAILABLE" => NoProviderMessage(context.RoleLevel),
                    "NO_ACTIVE_DEVICE" => requested.ErrorMessage!,
                    "PREMIUM_REQUIRED" => requested.ErrorMessage!,
                    "MUSIC_AUTH_FAILED" => requested.ErrorMessage!,
                    "MUSIC_FORBIDDEN" => requested.ErrorMessage!,
                    _ =>
                        $"Couldn't reach the music service for \"{query}\" — try again in a moment.",
                }
            );
        }

        MusicTrack track = requested.Value;

        string message = await _composer.ComposeAsync(
            new()
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

    /// <summary>
    /// "No active music provider" is a different problem for a different person: only the broadcaster
    /// can authorize a Spotify/YouTube connection (dashboard OAuth), so a viewer or mod telling them to
    /// "connect Spotify" is telling them to do something they cannot do. The broadcaster gets the
    /// actionable instruction; a mod gets told to flag it upward. A viewer gets no internal detail at
    /// all — to them the command simply reads as disabled, same as any other command they don't have
    /// the reward/config for.
    /// </summary>
    private static string NoProviderMessage(int roleLevel) =>
        roleLevel >= PermissionLevel.Broadcaster.ToLevelValue()
            ? "Song requests aren't connected yet — connect Spotify or YouTube in the dashboard."
        : roleLevel >= PermissionLevel.Moderator.ToLevelValue()
            ? "Song requests aren't connected — let the broadcaster know to connect Spotify or YouTube in the dashboard."
        : "This command is currently disabled.";
}
