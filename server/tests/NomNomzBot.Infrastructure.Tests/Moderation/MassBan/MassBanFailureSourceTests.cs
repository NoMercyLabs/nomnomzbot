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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Infrastructure.Notifications.Sources;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.MassBan;

/// <summary>Proves a finished mass-ban run with a refused account lands in the attention inbox, and a clean one does not.</summary>
public sealed class MassBanFailureSourceTests
{
    private static async Task<MassBanTestWorld> FinishedBatchAsync(bool refuseOne)
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        (
            await world
                .Consent()
                .RequestAsync(
                    MassBanTestWorld.Operator,
                    "Stoney",
                    [new("1001", "storm"), new("1002", "storm")],
                    new([], [])
                )
        )
            .IsSuccess.Should()
            .BeTrue();
        if (refuseOne)
            world
                .Moderation.BanAsOperatorAsync(
                    Arg.Any<Guid>(),
                    "own1",
                    "1002",
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Result.Failure<TwitchBanResult>(
                        "Twitch request failed (400).",
                        TwitchErrorCodes.TwitchError,
                        "The user may not be banned."
                    )
                );
        await world.Executor().RunAsync(maxBans: 50);
        return world;
    }

    [Fact]
    public async Task A_finished_run_with_a_refused_account_is_listed_with_the_counts()
    {
        MassBanTestWorld world = await FinishedBatchAsync(refuseOne: true);

        Result<List<ActionRequiredItemDto>> items = await new MassBanFailureSource(
            world.Db
        ).GetItemsAsync(world.OwnChannelId, new HashSet<string>());

        items.Value.Should().ContainSingle();
        ActionRequiredItemDto item = items.Value[0];
        item.Id.Should().StartWith("massban-failed:");
        item.Kind.Should().Be("mass_ban_failed");
        item.Count.Should().Be(1);
        item.Parameters.Should().Contain("channelLogin", "stoney");
        item.Parameters.Should().Contain("failedCount", "1");
        item.Parameters.Should().Contain("totalCount", "2");
        item.DetectedAt.Should().Be(world.Clock.GetUtcNow().UtcDateTime);

        Result<List<ActionRequiredItemDto>> dismissed = await new MassBanFailureSource(
            world.Db
        ).GetItemsAsync(world.OwnChannelId, new HashSet<string> { item.Id });
        dismissed.Value.Should().BeEmpty("a dismissed item stays hidden");

        Result<List<ActionRequiredItemDto>> elsewhere = await new MassBanFailureSource(
            world.Db
        ).GetItemsAsync(Guid.NewGuid(), new HashSet<string>());
        elsewhere.Value.Should().BeEmpty("another channel's inbox is not told");
    }

    [Fact]
    public async Task The_batch_report_counts_each_outcome_and_gives_the_reason_for_the_refused_account()
    {
        MassBanTestWorld world = await FinishedBatchAsync(refuseOne: true);

        IReadOnlyList<MassBanBatchReport> reports = await world
            .Consent()
            .ListBatchesAsync(MassBanTestWorld.Operator);

        reports.Should().ContainSingle();
        MassBanBatchReport report = reports[0];
        report.ChannelLogin.Should().Be("stoney");
        report.CompletedAt.Should().NotBeNull();
        report.Total.Should().Be(2);
        report.Banned.Should().Be(1);
        report.Failed.Should().Be(1);
        report.Retrying.Should().Be(0);
        report.Waiting.Should().Be(0);
        MassBanTargetReport refused = report.Targets.Should().ContainSingle().Subject;
        refused.TwitchUserId.Should().Be("1002");
        refused.Status.Should().Be(MassBanTargetStatus.Failed);
        refused.Attempts.Should().Be(1);
        refused.Error.Should().Contain("400");
        refused.NextAttemptAt.Should().BeNull();

        (await world.Consent().ListBatchesAsync(Guid.NewGuid()))
            .Should()
            .BeEmpty("another moderator's batches are not shown");
    }

    [Fact]
    public async Task A_run_that_banned_everyone_raises_nothing()
    {
        MassBanTestWorld world = await FinishedBatchAsync(refuseOne: false);

        Result<List<ActionRequiredItemDto>> items = await new MassBanFailureSource(
            world.Db
        ).GetItemsAsync(world.OwnChannelId, new HashSet<string>());

        items.Value.Should().BeEmpty();
    }
}
