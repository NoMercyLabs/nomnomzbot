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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!bansong</c> (legacy parity, S068c) — mod/broadcaster-only. Blocks the currently playing track from
/// future song requests by reading it off <see cref="IMusicService.GetNowPlayingAsync"/> and handing it to
/// <see cref="IBlockedTrackService"/>, the existing legacy <c>!bansong</c> list (music-sr.md) already
/// enforced on the song-request admission path — this builtin only adds the chat trigger, no new state.
/// </summary>
public sealed class BanSongBuiltin(
    IMusicService music,
    IBlockedTrackService blockedTracks,
    IBuiltinResponseComposer composer,
    MusicModerationGate gate
) : IBuiltinCommand
{
    public string BuiltinKey => "bansong";
    public int DefaultCooldownSeconds => 5;

    // Banning a track is a moderation action: a moderator badge or the music:queue:moderate grant (the
    // dashboard's ban-track action) passes. Checked in ExecuteAsync (MusicModerationGate), so the door
    // stays open for a non-mod chatter who holds the grant.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        if (!await gate.IsAllowedAsync(context, ct))
            return Result.Success(await composer.ComposePermissionDeniedAsync(context, ct));

        NowPlaying? nowPlaying = await music.GetNowPlayingAsync(
            context.BroadcasterId.ToString(),
            ct
        );

        if (nowPlaying is null || string.IsNullOrWhiteSpace(nowPlaying.TrackUri))
        {
            string nothing = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.BanSong.Key,
                    Slot = BuiltinResponseSlots.BanSong.Nothing,
                    NeutralFallback = "Nothing is playing right now — there's no track to ban.",
                },
                ct
            );
            return Result.Success(nothing);
        }

        Result<BlockedTrackDto> blocked = await blockedTracks.BlockAsync(
            context.BroadcasterId,
            new BlockTrackRequest(
                nowPlaying.Provider,
                nowPlaying.TrackUri,
                nowPlaying.TrackName ?? nowPlaying.TrackUri,
                Reason: "Banned via !bansong",
                BlockedByUserId: context.TriggeringUserId
            ),
            ct
        );

        if (blocked.IsFailure)
        {
            // The service's own sentence (a validation detail) stays in logs — chat gets the re-wordable slot.
            string couldNotBan = await composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.BanSong.Key,
                    Slot = BuiltinResponseSlots.BanSong.CouldNotBan,
                    NeutralFallback = "Could not ban that track — try again in a moment.",
                },
                ct
            );
            return Result.Success(couldNotBan);
        }

        string banned = await composer.ComposeAsync(
            context,
            BuiltinResponseSlots.BanSong.Key,
            BuiltinResponseSlots.BanSong.Banned,
            "@{user} banned \"{track.name}\" from song requests.",
            new Dictionary<string, string>
            {
                ["user"] = context.TriggeringUserDisplayName,
                ["track.name"] = blocked.Value.Title,
            },
            ct
        );
        return Result.Success(banned);
    }
}
