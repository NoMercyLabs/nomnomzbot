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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Rewards.Dtos;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// Proves the redemption countdown ("streamer does X for Y"): remaining time is CLOCK-derived (advancing
/// the clock shrinks it without any writes), pause freezes it exactly and resume continues from there, a
/// redelivered start is idempotent, manual complete AND clock expiry both mark the timer completed and
/// fulfill the redemption on Twitch, cancel never fulfills, and a Twitch fulfill failure still completes
/// the timer (graceful degradation — the countdown itself worked).
/// </summary>
public sealed class RedemptionTimerServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f5c00-4444-7000-8000-000000000001");
    private static readonly DateTimeOffset Start = new(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);

    private static (
        RedemptionTimerService Service,
        AuthDbContext Db,
        IRewardService Rewards,
        FakeTimeProvider Time
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IRewardService rewards = Substitute.For<IRewardService>();
        rewards
            .SetRedemptionStatusAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        FakeTimeProvider time = new(Start);
        RedemptionTimerService service = new(
            db,
            rewards,
            time,
            NullLogger<RedemptionTimerService>.Instance
        );
        return (service, db, rewards, time);
    }

    private static Task<Result<RedemptionTimerDto>> StartTimerAsync(
        RedemptionTimerService service,
        string redemptionId = "redemption-1",
        int seconds = 600
    ) => service.StartAsync(Tenant, redemptionId, "reward-1", "Do 20 pushups", "Alice", seconds);

    [Fact]
    public async Task Remaining_time_is_clock_derived_without_any_writes()
    {
        (RedemptionTimerService service, _, _, FakeTimeProvider time) = Build();
        await StartTimerAsync(service, seconds: 600);

        time.Advance(TimeSpan.FromSeconds(90));

        Result<IReadOnlyList<RedemptionTimerDto>> list = await service.ListAsync(Tenant.ToString());
        RedemptionTimerDto timer = list.Value.Single();
        timer.RemainingSeconds.Should().Be(510, "600s minus the 90s that elapsed on the clock");
        timer.Status.Should().Be("running");
        timer.DurationSeconds.Should().Be(600);
    }

    [Fact]
    public async Task A_redelivered_start_is_idempotent_per_redemption()
    {
        (RedemptionTimerService service, AuthDbContext db, _, FakeTimeProvider time) = Build();
        Result<RedemptionTimerDto> first = await StartTimerAsync(service);
        time.Advance(TimeSpan.FromSeconds(30));

        Result<RedemptionTimerDto> redelivered = await StartTimerAsync(service);

        redelivered.Value.Id.Should().Be(first.Value.Id, "the first timer wins");
        (await db.RedemptionTimers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Pause_freezes_the_remaining_time_and_resume_continues_from_it()
    {
        (RedemptionTimerService service, _, _, FakeTimeProvider time) = Build();
        Guid id = (await StartTimerAsync(service, seconds: 600)).Value.Id;

        time.Advance(TimeSpan.FromSeconds(100));
        Result<RedemptionTimerDto> paused = await service.PauseAsync(Tenant.ToString(), id);
        paused.Value.RemainingSeconds.Should().Be(500);

        // A paused timer does not bleed time.
        time.Advance(TimeSpan.FromMinutes(30));
        Result<IReadOnlyList<RedemptionTimerDto>> list = await service.ListAsync(Tenant.ToString());
        list.Value.Single().RemainingSeconds.Should().Be(500, "paused time never counts down");

        Result<RedemptionTimerDto> resumed = await service.ResumeAsync(Tenant.ToString(), id);
        resumed.Value.Status.Should().Be("running");
        time.Advance(TimeSpan.FromSeconds(200));
        (await service.ListAsync(Tenant.ToString()))
            .Value.Single()
            .RemainingSeconds.Should()
            .Be(300, "the countdown continues from the frozen 500s");
    }

    [Fact]
    public async Task Manual_complete_marks_completed_and_fulfills_the_redemption()
    {
        (RedemptionTimerService service, _, IRewardService rewards, _) = Build();
        Guid id = (await StartTimerAsync(service)).Value.Id;

        Result<RedemptionTimerDto> completed = await service.CompleteAsync(Tenant.ToString(), id);

        completed.Value.Status.Should().Be("completed");
        completed.Value.RemainingSeconds.Should().Be(0);
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Tenant.ToString(),
                "redemption-1",
                "FULFILLED",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cancel_stops_the_countdown_without_ever_touching_twitch()
    {
        (RedemptionTimerService service, _, IRewardService rewards, FakeTimeProvider time) =
            Build();
        Guid id = (await StartTimerAsync(service, seconds: 600)).Value.Id;
        time.Advance(TimeSpan.FromSeconds(60));

        Result<RedemptionTimerDto> canceled = await service.CancelAsync(Tenant.ToString(), id);

        canceled.Value.Status.Should().Be("canceled");
        canceled.Value.RemainingSeconds.Should().Be(540, "history keeps where it stopped");
        await rewards
            .DidNotReceiveWithAnyArgs()
            .SetRedemptionStatusAsync(default!, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expiry_completes_due_timers_and_fulfills_them_leaving_the_rest_running()
    {
        (
            RedemptionTimerService service,
            AuthDbContext db,
            IRewardService rewards,
            FakeTimeProvider time
        ) = Build();
        await StartTimerAsync(service, redemptionId: "short", seconds: 60);
        await StartTimerAsync(service, redemptionId: "long", seconds: 3600);

        time.Advance(TimeSpan.FromSeconds(61));
        int completed = await service.CompleteDueAsync();

        completed.Should().Be(1);
        RedemptionTimer shortTimer = await db.RedemptionTimers.SingleAsync(t =>
            t.RedemptionId == "short"
        );
        shortTimer.Status.Should().Be("completed");
        (await db.RedemptionTimers.SingleAsync(t => t.RedemptionId == "long"))
            .Status.Should()
            .Be("running");
        await rewards
            .Received(1)
            .SetRedemptionStatusAsync(
                Tenant.ToString(),
                "short",
                "FULFILLED",
                Arg.Any<CancellationToken>()
            );
        await rewards
            .DidNotReceive()
            .SetRedemptionStatusAsync(
                Tenant.ToString(),
                "long",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_twitch_fulfill_failure_still_completes_the_timer()
    {
        (RedemptionTimerService service, AuthDbContext db, IRewardService rewards, _) = Build();
        rewards
            .SetRedemptionStatusAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("not manageable", "FORBIDDEN"));
        Guid id = (await StartTimerAsync(service)).Value.Id;

        Result<RedemptionTimerDto> completed = await service.CompleteAsync(Tenant.ToString(), id);

        completed.IsSuccess.Should().BeTrue("the countdown itself worked — fulfillment degrades");
        (await db.RedemptionTimers.SingleAsync()).Status.Should().Be("completed");
    }

    [Fact]
    public async Task A_finished_timer_rejects_further_transitions()
    {
        (RedemptionTimerService service, _, _, _) = Build();
        Guid id = (await StartTimerAsync(service)).Value.Id;
        await service.CompleteAsync(Tenant.ToString(), id);

        (await service.PauseAsync(Tenant.ToString(), id)).IsFailure.Should().BeTrue();
        (await service.ResumeAsync(Tenant.ToString(), id)).IsFailure.Should().BeTrue();
        (await service.CancelAsync(Tenant.ToString(), id)).IsFailure.Should().BeTrue();
    }
}
