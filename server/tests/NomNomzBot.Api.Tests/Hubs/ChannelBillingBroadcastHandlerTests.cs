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

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Billing.Events;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>Proves a channel's billing changes reach that channel's dashboards as a <c>billing</c> config change.</summary>
public sealed class ChannelBillingBroadcastHandlerTests
{
    [Fact]
    public async Task A_tier_change_pushes_a_billing_config_change_to_the_channel_group()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();
        Guid subscription = Guid.CreateVersion7();

        await new ChannelBillingBroadcastHandler(notifier).HandleAsync(
            new SubscriptionTierChangedEvent
            {
                BroadcasterId = channel,
                SubscriptionId = subscription,
                FromTierKey = "free",
                ToTierKey = "pro",
                Status = "active",
                IsInviteOnlyGrant = false,
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Domain == "billing"
                    && dto.EntityId == subscription.ToString()
                    && dto.Action == "updated"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_founders_badge_grant_pushes_a_created_billing_change_without_an_entity()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        Guid channel = Guid.CreateVersion7();

        await new ChannelBillingBroadcastHandler(notifier).HandleAsync(
            new FoundersBadgeGrantedEvent
            {
                BroadcasterId = channel,
                FoundersBadgeId = Guid.CreateVersion7(),
            }
        );

        await notifier
            .Received(1)
            .SendConfigChangedAsync(
                channel.ToString(),
                Arg.Is<ConfigChangedDto>(dto =>
                    dto.Domain == "billing" && dto.EntityId == null && dto.Action == "created"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_level_event_without_a_channel_never_reaches_the_hub()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();

        await new ChannelBillingBroadcastHandler(notifier).HandleAsync(
            new FoundersBadgeGrantedEvent
            {
                BroadcasterId = Guid.Empty,
                FoundersBadgeId = Guid.CreateVersion7(),
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .SendConfigChangedAsync(default!, default!, default);
    }
}
