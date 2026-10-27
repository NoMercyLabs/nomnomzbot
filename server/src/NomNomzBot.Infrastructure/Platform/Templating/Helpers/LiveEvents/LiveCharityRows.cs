// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

/// <summary>The row factories of the <c>charity</c> namespace.</summary>
internal static class LiveCharityRows
{
    private const string Ns = "charity";

    /// <summary>A row over the active campaign; it prints nothing when the channel runs none.</summary>
    public static LiveEventRow Campaign(
        string key,
        string sample,
        Func<TwitchCharityCampaign, LiveEventCall, string?> print
    ) =>
        new(
            key,
            sample,
            Ns,
            data => Data(data).CampaignAsync(),
            async call =>
                call.Args.Count == 0 && await Data(call.Data).CampaignAsync() is { } campaign
                    ? print(campaign, call)
                    : null
        );

    /// <summary>A row over the newest page of donations; it prints nothing when the channel runs no campaign.</summary>
    public static LiveEventRow Donations(
        string key,
        string sample,
        Func<TwitchPage<TwitchCharityDonation>, LiveEventCall, string?> print
    ) =>
        new(
            key,
            sample,
            Ns,
            data => Data(data).DonationsAsync(),
            async call =>
                call.Args.Count == 0 && await Data(call.Data).DonationsAsync() is { } page
                    ? print(page, call)
                    : null
        );

    private static LiveCharityData Data(ILiveEventData data) => (LiveCharityData)data;
}
