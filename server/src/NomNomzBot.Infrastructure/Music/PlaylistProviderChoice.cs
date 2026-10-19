// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Music.PipelineActions;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>The service and the track a playlist step acts on.</summary>
internal sealed record PlaylistTarget(string Provider, string TrackUri);

/// <summary>
/// The music service a playlist step works on. A step carries an optional <c>provider</c> setting
/// (<c>spotify</c> or <c>youtube</c>); absent or blank means the channel's active provider, so a step saved
/// before the setting existed keeps doing exactly what it did. The playlist picker and the three playlist
/// actions all resolve the service through here.
/// </summary>
internal static class PlaylistProviderChoice
{
    public const string FieldName = "provider";

    public static readonly IReadOnlyList<string> Keys = ["spotify", "youtube"];

    /// <summary>The step form field that lets the streamer pick the service.</summary>
    public static PipelineActionFieldDescriptor Field() =>
        new(
            FieldName,
            PipelineActionFieldKind.Enum,
            Options: Keys,
            Description: new LocalizedText("pipeline.music_playlist.provider.help")
        );

    /// <summary>The chosen service, or the channel's active provider when none is chosen.</summary>
    public static async Task<Result<string>> ResolveAsync(
        IMusicService music,
        Guid broadcasterId,
        string? chosen,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(chosen))
            return await MusicControlResult.ResolveProviderAsync(music, broadcasterId, ct);

        string key = chosen.Trim().ToLowerInvariant();
        return Keys.Contains(key)
            ? Result.Success(key)
            : Result.Failure<string>($"unknown music provider '{chosen}'", "INVALID_PROVIDER");
    }

    /// <summary>
    /// The service a playlist step works on plus the currently playing track's URI, for the actions that act on
    /// the playing track. The failure message is what the action reports.
    /// </summary>
    public static async Task<Result<PlaylistTarget>> ResolveCurrentTrackAsync(
        IMusicService music,
        Guid broadcasterId,
        string? chosen,
        CancellationToken ct
    )
    {
        Result<string> provider = await ResolveAsync(music, broadcasterId, chosen, ct);
        if (provider.IsFailure)
            return Result.Failure<PlaylistTarget>(provider.ErrorCode ?? "CAPABILITY_UNSUPPORTED");

        Result<NowPlaying> nowPlaying = await MusicControlResult.ResolveNowPlayingAsync(
            music,
            broadcasterId,
            ct
        );
        if (nowPlaying.IsFailure || nowPlaying.Value.TrackUri is null)
            return Result.Failure<PlaylistTarget>("CAPABILITY_UNSUPPORTED");

        Result<NowPlaying> sameService = RequireTrackFrom(nowPlaying.Value, provider.Value);
        return sameService.IsFailure
            ? Result.Failure<PlaylistTarget>(sameService.ErrorMessage!)
            : Result.Success(new PlaylistTarget(provider.Value, nowPlaying.Value.TrackUri));
    }

    /// <summary>
    /// The playing track, only when it comes from <paramref name="provider"/>. A track of the other service has
    /// no meaning in this service's playlists, so it fails instead of adding or removing a wrong track.
    /// </summary>
    public static Result<NowPlaying> RequireTrackFrom(NowPlaying nowPlaying, string provider) =>
        string.Equals(nowPlaying.Provider, provider, StringComparison.OrdinalIgnoreCase)
            ? Result.Success(nowPlaying)
        : MusicControlResult.IsYouTubeItemOnSpotify(nowPlaying, provider)
            ? Result.Failure<NowPlaying>(
                MusicControlResult.NotSupportedForEngine,
                MusicControlResult.NotSupportedForEngine
            )
        : Result.Failure<NowPlaying>(
            $"the track playing now is from {nowPlaying.Provider}, not {provider}; give a {provider} track_uri or pick the {nowPlaying.Provider} service",
            "PROVIDER_MISMATCH"
        );
}
