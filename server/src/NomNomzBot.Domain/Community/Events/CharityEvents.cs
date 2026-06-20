// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Community.Events;

/// <summary>Published when a broadcaster starts a charity campaign (<c>channel.charity_campaign.start</c>).</summary>
public sealed class CharityCampaignStartedEvent : DomainEventBase
{
    public required string CampaignId { get; init; }
    public required string CharityName { get; init; }
    public string? Description { get; init; }
    public required int CurrentAmountValue { get; init; }
    public required int CurrentAmountDecimalPlaces { get; init; }
    public required string CurrentAmountCurrency { get; init; }
    public required int TargetAmountValue { get; init; }
    public required int TargetAmountDecimalPlaces { get; init; }
    public required string TargetAmountCurrency { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when progress is made toward a charity campaign's goal, or the broadcaster changes the goal
/// (<c>channel.charity_campaign.progress</c>).
/// </summary>
public sealed class CharityCampaignProgressEvent : DomainEventBase
{
    public required string CampaignId { get; init; }
    public required string CharityName { get; init; }
    public string? Description { get; init; }
    public required int CurrentAmountValue { get; init; }
    public required int CurrentAmountDecimalPlaces { get; init; }
    public required string CurrentAmountCurrency { get; init; }
    public required int TargetAmountValue { get; init; }
    public required int TargetAmountDecimalPlaces { get; init; }
    public required string TargetAmountCurrency { get; init; }
}

/// <summary>
/// Published when a user donates to the broadcaster's charity campaign (<c>channel.charity_campaign.donate</c>).
/// The donated <c>amount</c> is kept as Twitch sent it — integer minor units, decimal places, and currency code —
/// never pre-divided.
/// </summary>
public sealed class CharityDonationEvent : DomainEventBase
{
    public required string CampaignId { get; init; }
    public required string CharityName { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required string UserLogin { get; init; }
    public required int AmountValue { get; init; }
    public required int AmountDecimalPlaces { get; init; }
    public required string AmountCurrency { get; init; }
}

/// <summary>
/// Published when a broadcaster stops a charity campaign (<c>channel.charity_campaign.stop</c>); carries the
/// campaign's final running total.
/// </summary>
public sealed class CharityCampaignStoppedEvent : DomainEventBase
{
    public required string CampaignId { get; init; }
    public required string CharityName { get; init; }
    public string? Description { get; init; }
    public required int CurrentAmountValue { get; init; }
    public required int CurrentAmountDecimalPlaces { get; init; }
    public required string CurrentAmountCurrency { get; init; }
    public required int TargetAmountValue { get; init; }
    public required int TargetAmountDecimalPlaces { get; init; }
    public required string TargetAmountCurrency { get; init; }
    public required DateTimeOffset StoppedAt { get; init; }
}
