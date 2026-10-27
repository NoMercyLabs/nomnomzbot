// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>The player itself: playing or not, its state, volume, shuffle, repeat and the active provider.</summary>
internal sealed class MusicPlayerHelpers()
    : MusicHelperGroup(
        MusicRow.Custom(
            "music.now.isPlaying",
            "true",
            MusicSources.NowPlaying,
            async call => PeopleText.Flag(await call.Data.NowPlayingAsync() is { IsPlaying: true })
        ),
        MusicRow.Custom(
            "music.now.status",
            "playing",
            MusicSources.State,
            call => Task.FromResult<string?>(StatusOf(call.Data.State()))
        ),
        MusicRow.Now("music.volume", "60%", (now, _) => $"{now.Volume}%"),
        MusicRow.Now("music.shuffle", "off", (now, _) => now.ShuffleEnabled ? "on" : "off"),
        MusicRow.Now("music.repeat", "off", (now, _) => RepeatOf(now.RepeatMode)),
        MusicRow.Custom(
            "music.provider",
            "Spotify",
            MusicSources.Provider,
            async call =>
                await call.Data.ProviderAsync() is { Length: > 0 } key
                    ? PeopleText.PlatformName(key)
                    : null
        )
    )
{
    private static string StatusOf(PlaybackStateKind kind) =>
        kind switch
        {
            PlaybackStateKind.Playing => "playing",
            PlaybackStateKind.PausedByOwner => "paused",
            PlaybackStateKind.Starting => "starting",
            PlaybackStateKind.Blocked => "blocked",
            _ => "idle",
        };

    private static string RepeatOf(MusicRepeatMode mode) =>
        mode switch
        {
            MusicRepeatMode.Track => "track",
            MusicRepeatMode.Context => "context",
            _ => "off",
        };
}
