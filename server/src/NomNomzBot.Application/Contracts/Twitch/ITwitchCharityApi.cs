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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Charity" category sub-client: the broadcaster's active charity campaign and its donations
/// (twitch-helix.md §3.2). One of the grouped sub-clients exposed by <see cref="ITwitchHelixClient"/>.
/// Every method takes the owning tenant as a <see cref="Guid"/> and resolves it to the Twitch id internally
/// (the invariant: a Guid never reaches Twitch). Each returns <see cref="Result"/>/<see cref="Result{T}"/>
/// carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// </summary>
public interface ITwitchCharityApi
{
    /// <summary>
    /// Get Charity Campaign — the broadcaster's active campaign with its fundraising goal and amount raised.
    /// Returns <c>not_found</c> when no campaign is active. Requires <c>channel:read:charity</c>.
    /// </summary>
    Task<Result<TwitchCharityCampaign>> GetCharityCampaignAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Get Charity Campaign Donations — one page of donations to the active campaign. Requires <c>channel:read:charity</c>.</summary>
    Task<Result<TwitchPage<TwitchCharityDonation>>> GetCharityCampaignDonationsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );
}
