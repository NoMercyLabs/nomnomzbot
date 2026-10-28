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

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// The single custody path for a broadcaster's YouTube OAuth bearer — every YouTube surface that calls
/// the Data API on the user's own token (music manage, live-chat read) resolves it here instead of
/// re-implementing vault lookup + refresh. Reads the broadcaster's non-revoked <c>IntegrationConnection</c>
/// (Provider = "youtube") from <c>IIntegrationTokenVault</c>, decrypts the stored token, and transparently
/// refreshes it against Google when it is expiring, persisting the refreshed token back to the vault.
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
