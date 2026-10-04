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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the two reward update routes read a limit that the body leaves out differently: PUT replaces the whole
/// reward, so an absent limit is off (what the dashboard has always relied on when an operator blanks a limit
/// field); PATCH keeps an absent limit as it is. The request that reaches the service shows which.
/// </summary>
public sealed class RewardsControllerLimitsTests
{
    private static RewardsController Build(out IRewardService service)
    {
        service = Substitute.For<IRewardService>();
        service
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<UpdateRewardRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<RewardDetail>("stop here", "TEST"));
        return new RewardsController(
            service,
            Substitute.For<IRedemptionTimerService>(),
            Substitute.For<IApplicationDbContext>()
        );
    }

    private static UpdateRewardRequest SentTo(IRewardService service) =>
        service
            .ReceivedCalls()
            .Select(c => c.GetArguments().OfType<UpdateRewardRequest>().SingleOrDefault())
            .Single(r => r is not null)!;

    [Fact]
    public async Task Put_reads_a_limit_the_body_leaves_out_as_off_and_keeps_a_limit_it_sets()
    {
        RewardsController sut = Build(out IRewardService service);

        await sut.UpdateReward(
            "c",
            "r",
            new() { Cost = 500, MaxPerStream = 7 },
            CancellationToken.None
        );

        UpdateRewardRequest sent = SentTo(service);
        sent.Cost.Should().Be(500);
        sent.MaxPerStream.Should().Be(7);
        sent.MaxPerUserPerStream.Should().Be(0);
        sent.GlobalCooldownSeconds.Should().Be(0);
    }

    [Fact]
    public async Task Patch_passes_a_limit_the_body_leaves_out_through_as_absent()
    {
        RewardsController sut = Build(out IRewardService service);

        await sut.PatchReward("c", "r", new() { Cost = 500 }, CancellationToken.None);

        UpdateRewardRequest sent = SentTo(service);
        sent.MaxPerStream.Should().BeNull();
        sent.MaxPerUserPerStream.Should().BeNull();
        sent.GlobalCooldownSeconds.Should().BeNull();
    }
}
