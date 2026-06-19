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
/// Published when a channel point reward is redeemed by a viewer.
/// </summary>
public sealed class RewardRedeemedEvent : DomainEventBase
{
    public required string RewardId { get; init; }
    public required string RewardTitle { get; init; }
    public required string RedemptionId { get; init; }
    public required string UserId { get; init; }
    public required string UserDisplayName { get; init; }
    public required int Cost { get; init; }
    public string? UserInput { get; init; }
}
