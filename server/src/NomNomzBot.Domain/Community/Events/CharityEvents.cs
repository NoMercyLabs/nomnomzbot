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
    /// <summary>The id of the charity campaign.</summary>
    public required string CampaignId { get; init; }

    /// <summary>The name of the charity.</summary>
    public required string CharityName { get; init; }

    /// <summary>The description of the charity. Empty when none is set.</summary>
    public string? Description { get; init; }

    /// <summary>The amount raised so far, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int CurrentAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int CurrentAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the amount raised, as Twitch sends it.</summary>
    public required string CurrentAmountCurrency { get; init; }

    /// <summary>The campaign goal, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int TargetAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int TargetAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the campaign goal, as Twitch sends it.</summary>
    public required string TargetAmountCurrency { get; init; }

    /// <summary>When the campaign started, in UTC time.</summary>
    public required DateTimeOffset StartedAt { get; init; }
}

/// <summary>
/// Published when progress is made toward a charity campaign's goal, or the broadcaster changes the goal
/// (<c>channel.charity_campaign.progress</c>).
/// </summary>
public sealed class CharityCampaignProgressEvent : DomainEventBase
{
    /// <summary>The id of the charity campaign.</summary>
    public required string CampaignId { get; init; }

    /// <summary>The name of the charity.</summary>
    public required string CharityName { get; init; }

    /// <summary>The description of the charity. Empty when none is set.</summary>
    public string? Description { get; init; }

    /// <summary>The amount raised so far, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int CurrentAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int CurrentAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the amount raised, as Twitch sends it.</summary>
    public required string CurrentAmountCurrency { get; init; }

    /// <summary>The campaign goal, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int TargetAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int TargetAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the campaign goal, as Twitch sends it.</summary>
    public required string TargetAmountCurrency { get; init; }
}

/// <summary>
/// Published when a user donates to the broadcaster's charity campaign (<c>channel.charity_campaign.donate</c>).
/// The donated <c>amount</c> is kept as Twitch sent it — integer minor units, decimal places, and currency code —
/// never pre-divided.
/// </summary>
public sealed class CharityDonationEvent : DomainEventBase
{
    /// <summary>The id of the charity campaign.</summary>
    public required string CampaignId { get; init; }

    /// <summary>The name of the charity.</summary>
    public required string CharityName { get; init; }

    /// <summary>The Twitch user id of the viewer (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The display name of the viewer, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The login name of the viewer (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The donated amount, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int AmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int AmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the donation, as Twitch sends it.</summary>
    public required string AmountCurrency { get; init; }
}

/// <summary>
/// Published when a broadcaster stops a charity campaign (<c>channel.charity_campaign.stop</c>); carries the
/// campaign's final running total.
/// </summary>
public sealed class CharityCampaignStoppedEvent : DomainEventBase
{
    /// <summary>The id of the charity campaign.</summary>
    public required string CampaignId { get; init; }

    /// <summary>The name of the charity.</summary>
    public required string CharityName { get; init; }

    /// <summary>The description of the charity. Empty when none is set.</summary>
    public string? Description { get; init; }

    /// <summary>The amount raised so far, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int CurrentAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int CurrentAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the amount raised, as Twitch sends it.</summary>
    public required string CurrentAmountCurrency { get; init; }

    /// <summary>The campaign goal, in the smallest currency unit. Divide by 10 to the power of the decimal places to get the real amount.</summary>
    public required int TargetAmountValue { get; init; }

    /// <summary>How many decimal places the amount has (2 means the amount is in cents).</summary>
    public required int TargetAmountDecimalPlaces { get; init; }

    /// <summary>The currency code of the campaign goal, as Twitch sends it.</summary>
    public required string TargetAmountCurrency { get; init; }

    /// <summary>When the campaign stopped, in UTC time.</summary>
    public required DateTimeOffset StoppedAt { get; init; }
}
