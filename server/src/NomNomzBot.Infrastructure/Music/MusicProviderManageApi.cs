// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Capability-gating front for the §3.10 manage surface (music-sr.md). Resolves the provider by its
/// registry key, checks the member's required capability flag, then delegates to the provider's own
/// <see cref="IMusicProviderManageApi"/> implementation — a provider that supports management exposes
/// the surface itself. No provider-name checks anywhere: an unregistered key fails
/// <c>NOT_FOUND</c>, a missing capability (or a declared capability without a manage surface) fails
/// closed with <c>CAPABILITY_UNSUPPORTED</c>.
/// </summary>
public sealed class MusicProviderManageApi : IMusicProviderManageApi
{
    private readonly IEnumerable<IMusicProvider> _providers;

    public MusicProviderManageApi(IEnumerable<IMusicProvider> providers)
    {
        _providers = providers;
    }

    public async Task<Result<IReadOnlyList<MusicPlaylistDto>>> ListPlaylistsAsync(
        Guid broadcasterId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return Result.Failure<IReadOnlyList<MusicPlaylistDto>>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.ListPlaylistsAsync(broadcasterId, provider, cancellationToken);
    }

    public async Task<Result<MusicPlaylistDto>> CreatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        CreateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return Result.Failure<MusicPlaylistDto>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.CreatePlaylistAsync(
            broadcasterId,
            provider,
            request,
            cancellationToken
        );
    }

    public async Task<Result<MusicPlaylistDto>> UpdatePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        UpdateMusicPlaylistDto request,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return Result.Failure<MusicPlaylistDto>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.UpdatePlaylistAsync(
            broadcasterId,
            provider,
            playlistId,
            request,
            cancellationToken
        );
    }

    public async Task<Result> DeletePlaylistAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.DeletePlaylistAsync(
            broadcasterId,
            provider,
            playlistId,
            cancellationToken
        );
    }

    public async Task<Result> AddPlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.AddPlaylistTracksAsync(
            broadcasterId,
            provider,
            playlistId,
            trackUris,
            cancellationToken
        );
    }

    public async Task<Result> RemovePlaylistTracksAsync(
        Guid broadcasterId,
        string provider,
        string playlistId,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Playlists
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.RemovePlaylistTracksAsync(
            broadcasterId,
            provider,
            playlistId,
            trackUris,
            cancellationToken
        );
    }

    public async Task<Result> SaveTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Library
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.SaveTracksAsync(
            broadcasterId,
            provider,
            trackUris,
            cancellationToken
        );
    }

    public async Task<Result> RemoveSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Library
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.RemoveSavedTracksAsync(
            broadcasterId,
            provider,
            trackUris,
            cancellationToken
        );
    }

    public async Task<Result> RateTrackAsync(
        Guid broadcasterId,
        string provider,
        string trackUri,
        MusicRating rating,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Library
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.RateTrackAsync(
            broadcasterId,
            provider,
            trackUri,
            rating,
            cancellationToken
        );
    }

    public async Task<Result> FollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            FollowCapability(target)
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.FollowAsync(
            broadcasterId,
            provider,
            target,
            targetId,
            cancellationToken
        );
    }

    public async Task<Result> UnfollowAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        string targetId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            FollowCapability(target)
        );
        if (manage.IsFailure)
            return manage;

        return await manage.Value.UnfollowAsync(
            broadcasterId,
            provider,
            target,
            targetId,
            cancellationToken
        );
    }

    public async Task<Result<IReadOnlyList<TrackInfo>>> GetSavedTracksAsync(
        Guid broadcasterId,
        string provider,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Library
        );
        if (manage.IsFailure)
            return Result.Failure<IReadOnlyList<TrackInfo>>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.GetSavedTracksAsync(
            broadcasterId,
            provider,
            limit,
            offset,
            cancellationToken
        );
    }

    public async Task<Result<IReadOnlyList<bool>>> AreTracksSavedAsync(
        Guid broadcasterId,
        string provider,
        IReadOnlyList<string> trackUris,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            MusicProviderCapabilities.Library
        );
        if (manage.IsFailure)
            return Result.Failure<IReadOnlyList<bool>>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.AreTracksSavedAsync(
            broadcasterId,
            provider,
            trackUris,
            cancellationToken
        );
    }

    public async Task<Result<IReadOnlyList<MusicFollowDto>>> GetFollowedAsync(
        Guid broadcasterId,
        string provider,
        MusicFollowTarget target,
        int limit = 50,
        CancellationToken cancellationToken = default
    )
    {
        Result<IMusicProviderManageApi> manage = ResolveManageSurface(
            provider,
            FollowCapability(target)
        );
        if (manage.IsFailure)
            return Result.Failure<IReadOnlyList<MusicFollowDto>>(
                manage.ErrorMessage,
                manage.ErrorCode,
                manage.ErrorDetail
            );

        return await manage.Value.GetFollowedAsync(
            broadcasterId,
            provider,
            target,
            limit,
            cancellationToken
        );
    }

    // ─── Gating ──────────────────────────────────────────────────────────────

    /// <summary>Channel follows are provider subscriptions; artist/playlist follows are library writes (§3.10).</summary>
    private static MusicProviderCapabilities FollowCapability(MusicFollowTarget target) =>
        target == MusicFollowTarget.Channel
            ? MusicProviderCapabilities.Subscriptions
            : MusicProviderCapabilities.Library;

    private Result<IMusicProviderManageApi> ResolveManageSurface(
        string provider,
        MusicProviderCapabilities required
    )
    {
        IMusicProvider? registered = _providers.FirstOrDefault(p =>
            string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase)
        );
        if (registered is null)
            return Result.Failure<IMusicProviderManageApi>(
                $"Music provider '{provider}' is not registered.",
                "NOT_FOUND"
            );

        if ((registered.Capabilities & required) != required)
            return Result.Failure<IMusicProviderManageApi>(
                $"The '{registered.Provider}' provider does not support this operation.",
                "CAPABILITY_UNSUPPORTED"
            );

        // Fail closed: a declared capability without a manage surface is still unsupported.
        if (registered is not IMusicProviderManageApi manageSurface)
            return Result.Failure<IMusicProviderManageApi>(
                $"The '{registered.Provider}' provider does not expose a manage surface.",
                "CAPABILITY_UNSUPPORTED"
            );

        return Result.Success(manageSurface);
    }
}
