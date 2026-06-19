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

public sealed class BeforeRewardProcessedEvent : DomainEventBase
{
    public required string RewardId { get; init; }
    public required string RedemptionId { get; init; }
    public required string UserId { get; init; }
    public required string UserInput { get; init; }
}
