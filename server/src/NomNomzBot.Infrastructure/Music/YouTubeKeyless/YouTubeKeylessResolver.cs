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

namespace NomNomzBot.Infrastructure.Music.YouTubeKeyless;

/// <summary>
/// Title, channel and the embed flag come from oEmbed; length, the live flag, the age gate and the
/// "plays nowhere" flag come from the player data. Both calls run at the same time.
/// </summary>
public sealed class YouTubeKeylessResolver(
    IYouTubeOEmbedReader oEmbed,
    IYouTubePlayerDataReader playerData
) : IYouTubeKeylessResolver
{
    public async Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveAsync(
        string videoId,
        CancellationToken cancellationToken
    )
    {
        Task<YouTubeOEmbed> embedTask = oEmbed.ReadAsync(videoId, cancellationToken);
        Task<YouTubePlayerData> playerTask = playerData.ReadAsync(videoId, cancellationToken);
        await Task.WhenAll(embedTask, playerTask);
        YouTubeOEmbed embed = await embedTask;
        YouTubePlayerData player = await playerTask;

        switch (player.Status)
        {
            case YouTubePlayerDataStatus.Unreadable:
                return (null, MusicProviderFailureReason.Unavailable);
            case YouTubePlayerDataStatus.Missing:
                return (null, MusicProviderFailureReason.TrackMissing);
        }

        if (
            !YouTubeMusicCategory.IsMusic(player.Category, player.Author)
            && !YouTubeMusicCategory.IsMusic(player.Category, embed.Author)
        )
            return (null, MusicProviderFailureReason.NotMusic);

        switch (player.Status)
        {
            case YouTubePlayerDataStatus.AgeRestricted:
                return (
                    ToTrack(videoId, embed, player, ageRestricted: true),
                    MusicProviderFailureReason.None
                );
        }

        if (player.IsLive)
            return (null, MusicProviderFailureReason.LiveBroadcast);
        if (player.PlaysNowhere)
            return (null, MusicProviderFailureReason.NotPlayableInRegion);
        if (player.LengthSeconds <= 0)
            return (null, MusicProviderFailureReason.UnknownLength);

        return (
            ToTrack(videoId, embed, player, ageRestricted: false),
            MusicProviderFailureReason.None
        );
    }

    private static TrackInfo ToTrack(
        string videoId,
        YouTubeOEmbed embed,
        YouTubePlayerData player,
        bool ageRestricted
    ) =>
        new()
        {
            TrackName = embed.Title ?? player.Title ?? string.Empty,
            Artist = embed.Author ?? player.Author ?? string.Empty,
            Album = string.Empty,
            TrackUri = YouTubeMusicProvider.WatchUrl(videoId),
            AlbumArtUrl = embed.ThumbnailUrl ?? $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg",
            DurationMs = (int)Math.Min(player.LengthSeconds * 1000L, int.MaxValue),
            Provider = "youtube",
            ProviderTrackId = videoId,
            IsExplicit = false,
            IsAgeRestricted = ageRestricted,
            IsEmbeddable = embed.Status != YouTubeOEmbedStatus.NotEmbeddable,
        };
}
