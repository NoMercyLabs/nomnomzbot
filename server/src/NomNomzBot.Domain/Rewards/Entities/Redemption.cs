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

namespace NomNomzBot.Domain.Rewards.Entities;

/// <summary>
/// The channel-points redemption queue read model (rewards.md) — one row per Twitch redemption, folded by
/// <c>RewardRedemptionProjection</c> from the journal: a <c>RewardRedeemedEvent</c> upserts the row as
/// <c>unfulfilled</c>, a <c>RewardRedemptionUpdatedEvent</c> moves it to <c>fulfilled</c>/<c>canceled</c>. It is
/// the source for the Rewards page's redemption queue + fulfil/refund actions. Pure read model — rebuilt from the
/// journal, so a projection reset hard-clears the channel's rows and a replay re-folds them (no soft-delete).
/// </summary>
public class Redemption : ITenantScoped
{
    public long Id { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>The real Twitch redemption id (GUID) — unique within a channel; the projection's upsert key.</summary>
    public string RedemptionId { get; set; } = null!;
    public string RewardId { get; set; } = null!;
    public string RewardTitle { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string UserDisplayName { get; set; } = null!;
    public int Cost { get; set; }
    public string? UserInput { get; set; }

    /// <summary>The redemption status: <c>unfulfilled</c> (queued), <c>fulfilled</c>, or <c>canceled</c>.</summary>
    public string Status { get; set; } = null!;
    public DateTime RedeemedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
