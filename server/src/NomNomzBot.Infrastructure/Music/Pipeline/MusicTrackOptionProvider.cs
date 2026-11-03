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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Music.Pipeline;

/// <summary>
/// Supplies tracks found by searching the channel's music provider for the <c>music_track</c> picker kind. The saved
/// value is the track URI; the secondary text is the artist and album the provider reports. The list is a search, so
/// with no search text it is an empty list from a provider that was not asked, and a saved URI cannot be looked up by
/// itself: the step form shows the saved URI as it is. With no usable provider the list is unavailable, with the
/// reason.
/// </summary>
internal sealed class MusicTrackOptionProvider : IPipelineOptionProvider
{
    // The provider search returns the best matches in one call; this caps the call, not the dashboard's paging.
    private const int MaxSearchResults = 50;

    private readonly IMusicService _music;

    public MusicTrackOptionProvider(IMusicService music)
    {
        _music = music;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.MusicTrack;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string broadcasterKey = broadcasterId.ToString();
        string? reason = await MusicOptionSource.UnavailableReasonAsync(_music, broadcasterKey, ct);
        if (reason is not null)
            return PipelineOptionPage.Unavailable(reason);

        if (request.IsValueLookup || string.IsNullOrWhiteSpace(request.Search))
            return PipelineOptionPage.Empty();

        int wanted = Math.Min(
            MaxSearchResults,
            request.Pagination.Page * request.Pagination.PageSize
        );
        IReadOnlyList<MusicTrack> found = await _music.SearchAsync(
            broadcasterKey,
            request.Search,
            wanted,
            ct
        );

        List<PipelineOption> matches = [.. found.Select(ToOption)];
        return Result.Success(
            PipelineOptionListResult.Of(PipelineOptionPage.PageOf(matches, request), matches.Count)
        );
    }

    private static PipelineOption ToOption(MusicTrack track) =>
        PipelineOptionPage.Option(
            track.Uri,
            track.Name,
            string.IsNullOrWhiteSpace(track.Album)
                ? track.Artist
                : $"{track.Artist} - {track.Album}",
            track.ImageUrl
        );
}
