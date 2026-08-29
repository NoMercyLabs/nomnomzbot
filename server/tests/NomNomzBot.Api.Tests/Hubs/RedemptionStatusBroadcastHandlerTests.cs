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
/// Proves <see cref="RedemptionStatusBroadcastHandler"/> forwards a redemption status transition to dashboard
/// clients — the gap this closes: a fulfil/refund made from one dashboard session left every OTHER open session's
/// pending queue stale until a manual reload, because <see cref="RewardRedemptionUpdatedEvent"/> had no hub
/// broadcaster at all (S049).
/// </summary>
public sealed class RedemptionStatusBroadcastHandlerTests
{
    [Fact]
    public async Task HandleAsync_Fulfilled_ForwardsRedemptionAndRewardIdsWithStatus()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RedemptionStatusBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset occurredAt = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new RewardRedemptionUpdatedEvent
            {
                BroadcasterId = channel,
                OccurredAt = occurredAt,
                RedemptionId = "redemption-1",
                RewardId = "reward-1",
                RewardTitle = "Hydrate!",
                UserId = "user-1",
                UserDisplayName = "Viewer",
                Status = "fulfilled",
            }
        );

        await notifier
            .Received(1)
            .SendRedemptionStatusChangedAsync(
                channel.ToString(),
                Arg.Is<RedemptionStatusChangedDto>(dto =>
                    dto.BroadcasterId == channel.ToString()
                    && dto.RedemptionId == "redemption-1"
                    && dto.RewardId == "reward-1"
                    && dto.Status == "fulfilled"
                    && dto.Timestamp == occurredAt.ToString("O")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_Canceled_ForwardsCanceledStatus()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RedemptionStatusBroadcastHandler handler = new(notifier);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new RewardRedemptionUpdatedEvent
            {
                BroadcasterId = channel,
                RedemptionId = "redemption-2",
                RewardId = "reward-1",
                RewardTitle = "Hydrate!",
                UserId = "user-1",
                UserDisplayName = "Viewer",
                Status = "canceled",
            }
        );

        await notifier
            .Received(1)
            .SendRedemptionStatusChangedAsync(
                channel.ToString(),
                Arg.Is<RedemptionStatusChangedDto>(dto =>
                    dto.Status == "canceled" && dto.RedemptionId == "redemption-2"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HandleAsync_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        RedemptionStatusBroadcastHandler handler = new(notifier);

        await handler.HandleAsync(
            new RewardRedemptionUpdatedEvent
            {
                BroadcasterId = Guid.Empty,
                RedemptionId = "redemption-1",
                RewardId = "reward-1",
                RewardTitle = "t",
                UserId = "user-1",
                UserDisplayName = "Viewer",
                Status = "fulfilled",
            }
        );

        await notifier
            .DidNotReceive()
            .SendRedemptionStatusChangedAsync(
                Arg.Any<string>(),
                Arg.Any<RedemptionStatusChangedDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
