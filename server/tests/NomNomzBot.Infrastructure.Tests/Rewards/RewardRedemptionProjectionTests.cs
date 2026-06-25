// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Newtonsoft.Json;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the redemption-queue projection (rewards.md): a RewardRedeemedEvent queues an <c>unfulfilled</c> row
/// carrying the cost/input the add owns; a RewardRedemptionUpdatedEvent for the SAME Twitch redemption id folds
/// onto that same row and moves its status to fulfilled/canceled (it does not create a second row), so the queue
/// reflects the real status transition; and a reset hard-clears the channel's rows for a clean replay.
/// </summary>
public sealed class RewardRedemptionProjectionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000003001");
    private static readonly DateTime Now = new(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc);
    private static int _seq;

    private static EventRecord Event(string eventType, object payload) =>
        new(
            ++_seq,
            Guid.NewGuid(),
            Channel,
            _seq,
            eventType,
            1,
            "domain",
            JsonConvert.SerializeObject(payload),
            false,
            null,
            null,
            null,
            null,
            null,
            "{}",
            Now,
            Now
        );

    [Fact]
    public async Task A_redemption_add_queues_an_unfulfilled_row_with_its_cost_and_input()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        await new RewardRedemptionProjection(db).ApplyAsync(
            Event(
                "RewardRedeemedEvent",
                new
                {
                    RedemptionId = "redeem-1",
                    RewardId = "reward-1",
                    RewardTitle = "Hydrate!",
                    UserId = "u1",
                    UserDisplayName = "Buyer",
                    Cost = 100,
                    UserInput = "drink water",
                }
            )
        );

        Redemption row = db.Redemptions.Single();
        row.RedemptionId.Should().Be("redeem-1");
        row.RewardTitle.Should().Be("Hydrate!");
        row.UserDisplayName.Should().Be("Buyer");
        row.Cost.Should().Be(100);
        row.UserInput.Should().Be("drink water");
        row.Status.Should().Be("unfulfilled");
    }

    [Fact]
    public async Task A_status_update_folds_onto_the_same_row_and_moves_it_to_fulfilled()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RewardRedemptionProjection sut = new(db);

        await sut.ApplyAsync(
            Event(
                "RewardRedeemedEvent",
                new
                {
                    RedemptionId = "redeem-1",
                    RewardId = "reward-1",
                    RewardTitle = "Hydrate!",
                    UserId = "u1",
                    UserDisplayName = "Buyer",
                    Cost = 100,
                }
            )
        );
        await sut.ApplyAsync(
            Event(
                "RewardRedemptionUpdatedEvent",
                new
                {
                    RedemptionId = "redeem-1",
                    RewardId = "reward-1",
                    RewardTitle = "Hydrate!",
                    UserId = "u1",
                    UserDisplayName = "Buyer",
                    Status = "fulfilled",
                }
            )
        );

        Redemption row = db.Redemptions.Single(); // ONE row — the update folded onto the add, not a duplicate
        row.Status.Should().Be("fulfilled");
        row.Cost.Should().Be(100); // the add's cost survives the status transition
    }

    [Fact]
    public async Task Reset_hard_clears_the_channels_redemptions_for_replay()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RewardRedemptionProjection sut = new(db);
        await sut.ApplyAsync(
            Event(
                "RewardRedeemedEvent",
                new
                {
                    RedemptionId = "redeem-1",
                    RewardId = "reward-1",
                    RewardTitle = "X",
                    UserId = "u1",
                    UserDisplayName = "B",
                    Cost = 1,
                }
            )
        );

        await sut.ResetAsync(Channel);

        db.Redemptions.Should().BeEmpty();
    }
}
