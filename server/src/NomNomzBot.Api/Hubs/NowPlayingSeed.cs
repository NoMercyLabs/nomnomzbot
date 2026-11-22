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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The song that plays right now, in the shape of the live <c>now_playing</c> push, for a widget that just
/// subscribed: a page that loads between two songs shows the current one at once.
/// </summary>
public sealed class NowPlayingSeed(
    IMusicService music,
    IMusicHistoryReader history,
    TimeProvider clock
)
{
    public const string EventType = "now_playing";

    /// <summary>Null when the music service reports nothing.</summary>
    public async Task<NowPlayingWidgetPayload?> CurrentAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        NowPlaying? playing = await music.GetNowPlayingAsync(
            broadcasterId.ToString(),
            cancellationToken
        );
        if (playing is null)
            return null;

        return new(
            playing.IsPlaying,
            playing.TrackName,
            playing.Artist,
            playing.ImageUrl,
            playing.Provider,
            playing.TrackUri,
            playing.DurationMs,
            playing.ProgressMs,
            clock.GetUtcNow(),
            playing.RequestedBy,
            await RequesterColorAsync(playing.RequestedBy, cancellationToken)
        );
    }

    private async Task<string?> RequesterColorAsync(
        string? requestedBy,
        CancellationToken cancellationToken
    )
    {
        if (requestedBy is null)
            return null;

        Dictionary<string, RequesterIdentityDto> requesters = await history.ResolveRequestersAsync(
            [requestedBy],
            cancellationToken
        );
        return requesters.GetValueOrDefault(requestedBy)?.Color;
    }
}
