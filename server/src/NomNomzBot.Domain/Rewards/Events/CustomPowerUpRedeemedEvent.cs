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

namespace NomNomzBot.Domain.Rewards.Events;

/// <summary>
/// Published when a viewer redeems a streamer-defined custom Power-up
/// (<c>channel.custom_power_up_redemption.add</c>). A custom Power-up is priced in Bits (not channel
/// points) and is identified by the streamer's own Power-up id/title — the redemption carries the
/// optional viewer <see cref="UserInput"/> and the resolved Power-up cost in <see cref="Bits"/>.
/// </summary>
public sealed class CustomPowerUpRedeemedEvent : DomainEventBase
{
    /// <summary>The Twitch id of this redemption.</summary>
    public required string RedemptionId { get; init; }

    /// <summary>The Twitch user id of the viewer who redeemed (a number as text).</summary>
    public required string UserId { get; init; }

    /// <summary>The viewer's login name (lowercase).</summary>
    public required string UserLogin { get; init; }

    /// <summary>The viewer's display name, as shown in chat.</summary>
    public required string UserDisplayName { get; init; }

    /// <summary>The streamer's Power-up id this redemption targets.</summary>
    public required string PowerUpId { get; init; }

    /// <summary>The Power-up's title at redemption time.</summary>
    public required string PowerUpTitle { get; init; }

    /// <summary>The Bits cost paid for the Power-up.</summary>
    public required int Bits { get; init; }

    /// <summary>The redemption status, e.g. <c>unfulfilled</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The viewer-supplied input when the Power-up prompts for one; otherwise <c>null</c>.</summary>
    public string? UserInput { get; init; }
}
