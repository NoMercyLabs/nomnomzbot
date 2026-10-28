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
