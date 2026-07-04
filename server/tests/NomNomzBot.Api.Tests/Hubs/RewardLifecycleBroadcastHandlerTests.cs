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
/// Proves <see cref="RewardLifecycleBroadcastHandler"/> forwards reward CONFIG lifecycle (create/update/remove)
/// to dashboard clients with the right <c>Action</c> and fields on <see cref="RewardChangedDto"/> — the gap this
/// closes: the local <c>Reward</c> row was kept in sync but the rewards page never live-updated.
/// </summary>
public sealed class RewardLifecycleBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_RewardCreated_MapsActionAndFields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RewardLifecycleBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset occurredAt = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new RewardCreatedEvent
            {
                BroadcasterId = channel,
                OccurredAt = occurredAt,
                TwitchRewardId = "reward-1",
                Title = "Hydrate!",
                Cost = 500,
                IsEnabled = true,
            }
        );

        await notifier
            .Received(1)
            .SendRewardChangedAsync(
                channel.ToString(),
                Arg.Is<RewardChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.Action == "created"
                    && dto.RewardId == "reward-1"
                    && dto.Title == "Hydrate!"
                    && dto.Cost == 500
                    && dto.IsEnabled == true
                    && dto.Timestamp == occurredAt.ToString("O")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_RewardUpdated_MapsActionAndFields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RewardLifecycleBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new RewardUpdatedEvent
            {
                BroadcasterId = channel,
                TwitchRewardId = "reward-1",
                Title = "Hydrate more!",
                Cost = 750,
                IsEnabled = false,
            }
        );

        await notifier
            .Received(1)
            .SendRewardChangedAsync(
                channel.ToString(),
                Arg.Is<RewardChangedDto>(dto =>
                    dto.Action == "updated"
                    && dto.RewardId == "reward-1"
                    && dto.Title == "Hydrate more!"
                    && dto.Cost == 750
                    && dto.IsEnabled == false
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_RewardRemoved_MapsActionWithNullCostAndDisabled()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RewardLifecycleBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new RewardRemovedEvent
            {
                BroadcasterId = channel,
                TwitchRewardId = "reward-1",
                Title = "Hydrate!",
            }
        );

        await notifier
            .Received(1)
            .SendRewardChangedAsync(
                channel.ToString(),
                Arg.Is<RewardChangedDto>(dto =>
                    dto.Action == "removed"
                    && dto.RewardId == "reward-1"
                    && dto.Title == "Hydrate!"
                    && dto.Cost == null
                    && dto.IsEnabled == false
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RewardLifecycleBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new RewardCreatedEvent
            {
                BroadcasterId = Guid.Empty,
                TwitchRewardId = "reward-1",
                Title = "t",
                Cost = 1,
                IsEnabled = true,
            }
        );

        await notifier
            .DidNotReceive()
            .SendRewardChangedAsync(
                Arg.Any<string>(),
                Arg.Any<RewardChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
