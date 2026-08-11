// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.AutomationApi.Events;

/// <summary>
/// The single mapping from playback state to the wire-facing now-playing shape
/// (music-automation-controls.md §3.2/§3.3, D4) — used by BOTH the
/// <c>GET /automation/v1/music/now-playing</c> read and the <c>song.changed</c> automation event so
/// they can never drift. <see cref="AutomationNowPlayingDto.IsSaved"/> degrades to <c>null</c> (never a
/// failure) when the active provider lacks the <see cref="MusicProviderCapabilities.Library"/>
/// capability — the projection runs implicitly on every event, so it must never throw
/// <c>CAPABILITY_UNSUPPORTED</c> for a provider that simply doesn't support saving.
/// </summary>
public static class MusicAutomationProjection
{
    public static async Task<AutomationNowPlayingDto> ToNowPlayingAsync(
        NowPlaying nowPlaying,
        string provider,
        Guid broadcasterId,
        IMusicProviderManageApi manageApi,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        bool? isSaved = null;
        if (nowPlaying.TrackUri is string trackUri)
        {
            Application.Common.Models.Result<IReadOnlyList<bool>> savedCheck =
                await manageApi.AreTracksSavedAsync(broadcasterId, provider, [trackUri], ct);
            if (savedCheck.IsSuccess && savedCheck.Value.Count > 0)
                isSaved = savedCheck.Value[0];
        }

        return new AutomationNowPlayingDto(
            nowPlaying.TrackName,
            nowPlaying.Artist,
            nowPlaying.DurationMs,
            nowPlaying.ProgressMs,
            nowPlaying.IsPlaying,
            nowPlaying.ShuffleEnabled,
            nowPlaying.RepeatMode.ToString().ToLowerInvariant(),
            isSaved,
            timeProvider.GetUtcNow(),
            nowPlaying.Volume
        );
    }
}
