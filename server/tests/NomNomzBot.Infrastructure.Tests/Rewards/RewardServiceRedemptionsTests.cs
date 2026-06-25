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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves <see cref="RewardService.ListRedemptionsAsync"/> serves the redemption queue read model: it filters to
/// the requested status (the pending lane is <c>unfulfilled</c>), orders newest-first, and projects each row to
/// the API DTO carrying the fields the Rewards page shows (reward title, viewer, cost, status). A null status
/// returns the whole queue.
/// </summary>
public sealed class RewardServiceRedemptionsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d701");

    private static RewardService Build(AuthDbContext db) =>
        new(db, Substitute.For<ITwitchChannelPointsApi>(), NullLogger<RewardService>.Instance);

    private static Redemption Redeem(string id, string status, DateTime at) =>
        new()
        {
            BroadcasterId = Channel,
            RedemptionId = id,
            RewardId = "rw1",
            RewardTitle = "Spotify Song Request",
            UserId = "u1",
            UserDisplayName = "Stoney_Eagle",
            Cost = 50,
            UserInput = "a link",
            Status = status,
            RedeemedAt = at,
        };

    [Fact]
    public async Task ListRedemptions_filters_to_the_requested_status_newest_first()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Redemptions.AddRange(
            Redeem("r1", "unfulfilled", new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
            Redeem("r2", "fulfilled", new DateTime(2025, 8, 2, 0, 0, 0, DateTimeKind.Utc)),
            Redeem("r3", "unfulfilled", new DateTime(2025, 8, 3, 0, 0, 0, DateTimeKind.Utc))
        );
        await db.SaveChangesAsync();

        Result<PagedList<RedemptionListItem>> result = await Build(db)
            .ListRedemptionsAsync(Channel.ToString(), "unfulfilled", new PaginationParams(1, 25));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TotalCount.Should().Be(2); // only the two unfulfilled, not the fulfilled one
        result.Value.Items.Select(i => i.RedemptionId).Should().ContainInOrder("r3", "r1"); // newest RedeemedAt first
        result.Value.Items.Should().OnlyContain(i => i.Status == "unfulfilled");
        result.Value.Items[0].RewardTitle.Should().Be("Spotify Song Request");
        result.Value.Items[0].UserDisplayName.Should().Be("Stoney_Eagle");
        result.Value.Items[0].Cost.Should().Be(50);
    }

    [Fact]
    public async Task ListRedemptions_without_a_status_returns_the_whole_queue()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Redemptions.AddRange(
            Redeem("r1", "unfulfilled", new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc)),
            Redeem("r2", "fulfilled", new DateTime(2025, 8, 2, 0, 0, 0, DateTimeKind.Utc))
        );
        await db.SaveChangesAsync();

        Result<PagedList<RedemptionListItem>> result = await Build(db)
            .ListRedemptionsAsync(Channel.ToString(), status: null, new PaginationParams(1, 25));

        result.Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ListRedemptions_rejects_a_non_guid_channel_id()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        Result<PagedList<RedemptionListItem>> result = await Build(db)
            .ListRedemptionsAsync("not-a-guid", null, new PaginationParams(1, 25));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task SetRedemptionStatus_fulfils_via_helix_and_marks_the_local_row_fulfilled()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Redemptions.Add(
            Redeem("redeem-1", "unfulfilled", new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc))
        );
        await db.SaveChangesAsync();

        ITwitchChannelPointsApi points = Substitute.For<ITwitchChannelPointsApi>();
        points
            .UpdateRedemptionStatusAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<UpdateRedemptionStatusRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchCustomRewardRedemption>>(
                    Array.Empty<TwitchCustomRewardRedemption>()
                )
            );
        RewardService sut = new(db, points, NullLogger<RewardService>.Instance);

        Result result = await sut.SetRedemptionStatusAsync(
            Channel.ToString(),
            "redeem-1",
            "FULFILLED"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // Helix is addressed by the row's reward id + the redemption id, with the FULFILLED status.
        await points
            .Received(1)
            .UpdateRedemptionStatusAsync(
                Channel,
                "rw1",
                Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 1 && ids[0] == "redeem-1"),
                Arg.Is<UpdateRedemptionStatusRequest>(r => r.Status == "FULFILLED"),
                Arg.Any<CancellationToken>()
            );
        // The local queue row reflects the new status immediately (optimistic; the EventSub fold confirms it).
        db.Redemptions.Single().Status.Should().Be("fulfilled");
    }

    [Fact]
    public async Task SetRedemptionStatus_returns_not_found_for_an_unknown_redemption()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RewardService sut = new(
            db,
            Substitute.For<ITwitchChannelPointsApi>(),
            NullLogger<RewardService>.Instance
        );

        Result result = await sut.SetRedemptionStatusAsync(Channel.ToString(), "nope", "FULFILLED");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
