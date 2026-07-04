// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>Broadcasts channel point reward redemptions to dashboard clients.</summary>
public sealed class RewardRedeemedBroadcastHandler : IEventHandler<RewardRedeemedEvent>
{
    private readonly IDashboardNotifier _notifier;
    private readonly IHubUserEnricher _enricher;

    public RewardRedeemedBroadcastHandler(IDashboardNotifier notifier, IHubUserEnricher enricher)
    {
        _notifier = notifier;
        _enricher = enricher;
    }

    public async Task HandleAsync(RewardRedeemedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
            @event.BroadcasterId,
            @event.UserId,
            ct
        );

        RewardRedeemedDto dto = new(
            BroadcasterId: @event.BroadcasterId.ToString(),
            RewardId: @event.RewardId,
            RewardTitle: @event.RewardTitle,
            RedemptionId: @event.RedemptionId,
            UserId: @event.UserId,
            UserDisplayName: @event.UserDisplayName,
            Cost: @event.Cost,
            UserInput: @event.UserInput,
            Timestamp: @event.OccurredAt.ToString("O"),
            AvatarUrl: enrichment?.AvatarUrl,
            Pronouns: enrichment?.Pronouns,
            CommunityStanding: enrichment?.CommunityStanding
        );

        await _notifier.SendRewardRedeemedAsync(@event.BroadcasterId.ToString(), dto, ct);
    }
}
