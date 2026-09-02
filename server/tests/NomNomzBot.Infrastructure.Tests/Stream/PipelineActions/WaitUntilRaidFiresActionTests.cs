// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Stream.PipelineActions;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves <c>wait_until_raid_fires</c> re-anchors to the deadline <c>start_raid</c> stamps into
/// <c>raid.fires_at_utc_ticks</c> instead of trusting a blind sum of fixed waits — the fix for drift
/// that a slow OBS/chat call between the raid firing and this step would otherwise introduce.
///
/// The capping/negative/missing-variable math is asserted directly against
/// <see cref="WaitUntilRaidFiresAction.ComputeWait"/> — a pure function — rather than by actually
/// sleeping through a bogus far-future deadline in a unit test.
/// </summary>
public sealed class WaitUntilRaidFiresActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b302");
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, string> Vars(string ticks) =>
        new(StringComparer.OrdinalIgnoreCase) { ["raid.fires_at_utc_ticks"] = ticks };

    [Fact]
    public void No_recorded_deadline_returns_null_not_zero()
    {
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(
            new Dictionary<string, string>(),
            Now
        );

        // Must be null, not TimeSpan.Zero — a caller distinguishing "nothing recorded" from "deadline
        // already hit" needs these to read differently, even though both currently no-op.
        wait.Should().BeNull();
    }

    [Fact]
    public void A_corrupt_ticks_value_returns_null()
    {
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars("not-a-number"), Now);

        wait.Should().BeNull();
    }

    [Fact]
    public void A_deadline_thirty_seconds_out_waits_exactly_thirty_seconds()
    {
        DateTime firesAt = Now.AddSeconds(30);
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars(firesAt.Ticks.ToString()), Now);

        wait.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_deadline_one_second_in_the_past_returns_zero_not_negative()
    {
        DateTime firesAt = Now.AddSeconds(-1);
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars(firesAt.Ticks.ToString()), Now);

        wait.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_deadline_exactly_ninety_one_seconds_out_is_capped_to_ninety()
    {
        // One second past the cap boundary — proves the clamp actually engages rather than just
        // happening to match an already-in-range value.
        DateTime firesAt = Now.AddSeconds(91);
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars(firesAt.Ticks.ToString()), Now);

        wait.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void A_deadline_exactly_at_the_ninety_second_cap_is_returned_uncapped()
    {
        // The boundary itself: must pass through as exactly 90s, not be nudged by an off-by-one in the
        // clamp comparison (> vs >=).
        DateTime firesAt = Now.AddSeconds(90);
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars(firesAt.Ticks.ToString()), Now);

        wait.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void A_deadline_an_hour_out_is_capped_to_ninety_seconds()
    {
        DateTime firesAt = Now.AddHours(1);
        TimeSpan? wait = WaitUntilRaidFiresAction.ComputeWait(Vars(firesAt.Ticks.ToString()), Now);

        wait.Should().Be(TimeSpan.FromSeconds(90));
    }

    // ── ExecuteAsync: proves the computed value is actually what gets awaited, and that the action
    //    reports success rather than failure in every branch. ─────────────────────────────────────

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!raid target",
        };

    private static readonly ActionDefinition Action = new()
    {
        Type = "wait_until_raid_fires",
        Parameters = new(),
    };

    [Fact]
    public async Task Waits_out_the_remaining_time_to_the_recorded_deadline()
    {
        WaitUntilRaidFiresAction sut = new();
        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["raid.fires_at_utc_ticks"] = DateTime
            .UtcNow.AddMilliseconds(300)
            .Ticks.ToString();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ActionResult result = await sut.ExecuteAsync(ctx, Action);
        stopwatch.Stop();

        result.Succeeded.Should().BeTrue();
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_deadline_already_in_the_past_returns_immediately()
    {
        WaitUntilRaidFiresAction sut = new();
        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["raid.fires_at_utc_ticks"] = DateTime.UtcNow.AddSeconds(-5).Ticks.ToString();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ActionResult result = await sut.ExecuteAsync(ctx, Action);
        stopwatch.Stop();

        result.Succeeded.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task No_recorded_deadline_is_a_no_op_not_a_failure()
    {
        WaitUntilRaidFiresAction sut = new();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ActionResult result = await sut.ExecuteAsync(Ctx(), Action);
        stopwatch.Stop();

        result.Succeeded.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task A_corrupt_deadline_value_is_a_no_op_not_a_crash()
    {
        WaitUntilRaidFiresAction sut = new();
        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["raid.fires_at_utc_ticks"] = "not-a-number";

        ActionResult result = await sut.ExecuteAsync(ctx, Action);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_cancelled_token_during_the_wait_propagates_rather_than_being_swallowed()
    {
        WaitUntilRaidFiresAction sut = new();
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!raid target",
        };
        ctx.Variables["raid.fires_at_utc_ticks"] = DateTime.UtcNow.AddSeconds(5).Ticks.ToString();
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(100));

        PipelineExecutionContext cancellableCtx = new()
        {
            BroadcasterId = ctx.BroadcasterId,
            TriggeredByUserId = ctx.TriggeredByUserId,
            TriggeredByDisplayName = ctx.TriggeredByDisplayName,
            MessageId = ctx.MessageId,
            RawMessage = ctx.RawMessage,
            CancellationToken = cts.Token,
        };
        cancellableCtx.Variables["raid.fires_at_utc_ticks"] = ctx.Variables[
            "raid.fires_at_utc_ticks"
        ];

        Func<Task> act = async () => await sut.ExecuteAsync(cancellableCtx, Action);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
