// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Charity" category wire models (GET /charity/campaigns, /charity/donations). These records
// deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no per-property
// annotations. Twitch ids stay strings (campaign / user ids); the owning tenant is always passed in as a
// Guid method argument, never here.

/// <summary>A monetary amount in the campaign's currency, expressed as a minor-unit integer plus its scale.</summary>
public sealed record TwitchCharityAmount(int Value, int DecimalPlaces, string Currency);

/// <summary>Get Charity Campaign — the broadcaster's active charity campaign, its goal, and the amount raised.</summary>
public sealed record TwitchCharityCampaign(
    string Id,
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string CharityName,
    string CharityDescription,
    string CharityLogo,
    string CharityWebsite,
    TwitchCharityAmount CurrentAmount,
    TwitchCharityAmount TargetAmount
);

/// <summary>One donation a user made to the broadcaster's active charity campaign (Get Charity Campaign Donations).</summary>
public sealed record TwitchCharityDonation(
    string Id,
    string CampaignId,
    string UserId,
    string UserLogin,
    string UserName,
    TwitchCharityAmount Amount
);
