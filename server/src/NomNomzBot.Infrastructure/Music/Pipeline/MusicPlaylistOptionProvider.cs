// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Music.Services;
using MusicPlaylistDto = NomNomzBot.Application.Contracts.Music.MusicPlaylistDto;

namespace NomNomzBot.Infrastructure.Music.Pipeline;

/// <summary>
/// Supplies the playlists of the channel's connected music provider for the <c>music_playlist</c>
/// resource-picker kind (Spotify: GET /me/playlists through <see cref="IMusicProviderManageApi"/>). The option
/// value is the provider's playlist id. With no connected provider, or a failed read, the list comes back
/// unavailable with the reason instead of empty.
/// </summary>
internal sealed class MusicPlaylistOptionProvider : IPipelineOptionProvider
{
    private readonly IMusicService _music;
    private readonly IMusicProviderManageApi _manage;

    public MusicPlaylistOptionProvider(IMusicService music, IMusicProviderManageApi manage)
    {
        _music = music;
        _manage = manage;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.MusicPlaylist;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<string> provider = await PlaylistProviderChoice.ResolveAsync(
            _music,
            broadcasterId,
            request.ContextValue(PlaylistProviderChoice.FieldName),
            ct
        );
        if (provider.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(
                    provider.ErrorCode == "INVALID_PROVIDER"
                        ? provider.ErrorMessage!
                        : "No music provider is connected."
                )
            );

        Result<IReadOnlyList<MusicPlaylistDto>> listed = await _manage.ListPlaylistsAsync(
            broadcasterId,
            provider.Value,
            ct
        );
        if (listed.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(
                    listed.ErrorMessage ?? "Could not read the playlists from the music provider."
                )
            );

        IEnumerable<MusicPlaylistDto> filtered = listed.Value;
        if (request.IsValueLookup)
            filtered = filtered.Where(p =>
                string.Equals(p.Id, request.Value, StringComparison.Ordinal)
            );
        else if (!string.IsNullOrWhiteSpace(request.Search))
            filtered = filtered.Where(p =>
                p.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
            );

        List<MusicPlaylistDto> ordered = [.. filtered.OrderBy(p => p.Name)];
        List<PipelineOption> page =
        [
            .. ordered
                .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
                .Take(request.Pagination.PageSize)
                .Select(ToOption),
        ];

        return Result.Success(PipelineOptionListResult.Of(page, ordered.Count));
    }

    private static PipelineOption ToOption(MusicPlaylistDto playlist) =>
        new(
            playlist.Id,
            playlist.Name,
            $"{playlist.TrackCount} tracks · {playlist.Provider}",
            playlist.ImageUrl,
            PipelineOptionState.Selectable
        );
}
