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
using NomNomzBot.Application.Music.Dtos;

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// Public SR-page tokens (music-sr.md §3.7). A per-channel opaque, rotatable capability token — distinct from
/// <c>Channels.OverlayToken</c> (OBS sources) — that lets the public <c>/sr/{token}</c> page resolve a channel and
/// submit requests without a JWT. Backed by the <c>Channels.SongRequestPageToken</c> column; not PII.
/// </summary>
public interface ISongRequestPageTokenService
{
    /// <summary>Resolves an SR-page token to its channel context; <c>NOT_FOUND</c> for an unknown/disabled token.</summary>
    Task<Result<SongRequestPageDto>> ResolveAsync(
        string pageToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Returns the channel's SR-page token, minting one (opaque, not PII) on first call. Idempotent.</summary>
    Task<Result<string>> GetOrCreateAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Invalidates the old token and returns a fresh one (revokes public access via the old link).</summary>
    Task<Result<string>> RotateAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
