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

namespace NomNomzBot.Application.Assets.Services;

/// <summary>
/// The channel media asset library: broadcaster-uploaded images/audio that overlay widgets reference by
/// stable URL. Upload is create-or-REPLACE by <c>Name</c> (one live row per name — the serving URL never
/// moves); content type is sniffed, never trusted; caps: 8 MB per asset, 64 MB per channel.
/// </summary>
public interface IChannelAssetService
{
    Task<Result<PagedList<ChannelAssetDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    );

    Task<Result<ChannelAssetDto>> GetAsync(
        Guid broadcasterId,
        Guid id,
        CancellationToken ct = default
    );

    /// <summary>
    /// Validates format (content sniff) and both size caps, stores the blob via
    /// <see cref="IChannelAssetStore"/>, and persists the metadata row — replacing an existing asset with
    /// the same <c>Name</c> in place (its old blob is removed).
    /// </summary>
    Task<Result<ChannelAssetDto>> UploadAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UploadChannelAssetRequest request,
        CancellationToken ct = default
    );

    Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid id,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves a live asset for the anonymous public serving route (OBS browser sources) — by channel and
    /// name, bypassing the ambient tenant (there is none on an anonymous request).
    /// </summary>
    Task<Result<ChannelAssetContent>> OpenForServingAsync(
        Guid broadcasterId,
        string name,
        CancellationToken ct = default
    );
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public sealed record UploadChannelAssetRequest(
    string Name,
    string DisplayName,
    string FileName,
    System.IO.Stream Content
);

public sealed record ChannelAssetDto(
    Guid Id,
    string Name,
    string DisplayName,
    string Kind,
    string MimeType,
    long SizeBytes,
    DateTime CreatedAt,
    // Stable, anonymous, relative serving URL (`/api/v1/assets/file/{channelId}/{name}`) with a `?v=`
    // cache-buster — the value widget configs store and the dashboard's copy-URL button copies.
    string Url
);

/// <summary>An open asset stream plus the sniffed MIME type, for the public serving route.</summary>
public sealed record ChannelAssetContent(
    System.IO.Stream Content,
    string MimeType,
    string Kind,
    long SizeBytes
);
