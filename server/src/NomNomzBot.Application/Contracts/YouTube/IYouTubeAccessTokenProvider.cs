// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// The single custody path for a broadcaster's YouTube OAuth bearer — every YouTube surface that calls
/// the Data API on the user's own token (music manage, live-chat read) resolves it here instead of
/// re-implementing vault lookup + refresh. Reads the channel's enabled <c>Service</c> row
/// (Name = "youtube"), unprotects the stored token, and transparently refreshes it against Google when
/// it is expiring, persisting the rotated token back to the vault.
/// </summary>
public interface IYouTubeAccessTokenProvider
{
    /// <summary>
    /// The broadcaster's current YouTube access token, refreshed when expiring — or <c>null</c> when the
    /// channel has no enabled YouTube connection, the vault cannot unprotect the stored token, or the
    /// refresh grant fails (the caller degrades to its not-connected path; this never throws).
    /// </summary>
    Task<string?> GetAccessTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
