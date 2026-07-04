// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Rewards.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="RewardRedeemedBroadcastHandler"/> carries the GAP E3-2 hub-broadcast-layer enrichment
/// additively on <see cref="RewardRedeemedDto"/> — populated when the enricher has data, and <c>null</c> (never
/// a crash) when it doesn't.
/// </summary>
public sealed class RewardRedeemedBroadcastHandlerTests
{
    [Fact]
    public async Task Redemption_with_known_viewer_carries_the_enriched_fields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(new HubUserEnrichment("Stoney", "https://cdn/avatar.png", "she/her", "Vip"));
        RewardRedeemedBroadcastHandler handler = new(notifier, enricher);

        await handler.HandleAsync(
            new RewardRedeemedEvent
            {
                BroadcasterId = channel,
                RewardId = "r1",
                RewardTitle = "Hydrate",
                RedemptionId = "red1",
                UserId = "u1",
                UserDisplayName = "Stoney",
                Cost = 100,
            }
        );

        await notifier
            .Received(1)
            .SendRewardRedeemedAsync(
                channel.ToString(),
                Arg.Is<RewardRedeemedDto>(dto =>
                    dto.AvatarUrl == "https://cdn/avatar.png"
                    && dto.Pronouns == "she/her"
                    && dto.CommunityStanding == "Vip"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Redemption_with_unknown_viewer_carries_null_enrichment_not_a_crash()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns((HubUserEnrichment?)null);
        RewardRedeemedBroadcastHandler handler = new(notifier, enricher);

        await handler.HandleAsync(
            new RewardRedeemedEvent
            {
                BroadcasterId = channel,
                RewardId = "r1",
                RewardTitle = "Hydrate",
                RedemptionId = "red1",
                UserId = "u1",
                UserDisplayName = "Stoney",
                Cost = 100,
            }
        );

        await notifier
            .Received(1)
            .SendRewardRedeemedAsync(
                channel.ToString(),
                Arg.Is<RewardRedeemedDto>(dto =>
                    dto.AvatarUrl == null && dto.Pronouns == null && dto.CommunityStanding == null
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
