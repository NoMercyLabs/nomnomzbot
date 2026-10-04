// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Tests.Common;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves a fired scheduled run never waits on another channel's run: a blocked run of channel A leaves channel B's
/// run (and the next sweep) free to reach the engine, runs of one channel keep their due order and never overlap,
/// at most <see cref="ScheduledPipelineDispatcher.MaxConcurrentRuns"/> runs are inside the engine at once, and
/// shutdown waits for in-flight runs, then cancels them after the drain timeout on the fake clock.
/// </summary>
public sealed class ScheduledPipelineDispatchTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Start = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Time allowed for a run that must NOT start to show up wrongly. A wrong implementation starts such a run
    /// within microseconds, so this only ever delays a passing test, never makes a failing one pass by luck of
    /// ordering in the other direction.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    /// <summary>An engine whose runs each block until the test releases that pipeline.</summary>
    private sealed class BlockingEngine
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _gates = new();
        private readonly ConcurrentQueue<string> _timeline = new();
        private int _inside;
        private int _maxInside;
        private int _cancelled;

        public IPipelineEngine Engine { get; }
        public IReadOnlyCollection<string> Timeline => _timeline;
        public int MaxInside => Volatile.Read(ref _maxInside);
        public int Cancelled => Volatile.Read(ref _cancelled);

        public BlockingEngine()
        {
            Engine = Substitute.For<IPipelineEngine>();
            Engine
                .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                    RunAsync(call.Arg<PipelineRequest>(), call.Arg<CancellationToken>())
                );
        }

        public bool Started(Guid pipelineId) => _timeline.Contains($"start:{pipelineId}");

        public int StartedCount => _timeline.Count(e => e.StartsWith("start:"));

        public void Release(Guid pipelineId) => Gate(pipelineId).TrySetResult();

        private TaskCompletionSource Gate(Guid pipelineId) =>
            _gates.GetOrAdd(
                pipelineId,
                _ => new(TaskCreationOptions.RunContinuationsAsynchronously)
            );

        private async Task<PipelineExecutionResult> RunAsync(
            PipelineRequest request,
            CancellationToken ct
        )
        {
            Guid id = request.PipelineId!.Value;
            int now = Interlocked.Increment(ref _inside);
            int seen;
            do
            {
                seen = Volatile.Read(ref _maxInside);
            } while (now > seen && Interlocked.CompareExchange(ref _maxInside, now, seen) != seen);

            _timeline.Enqueue($"start:{id}");
            try
            {
                await Gate(id).Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
            finally
            {
                _timeline.Enqueue($"end:{id}");
                Interlocked.Decrement(ref _inside);
            }

            return new()
            {
                ExecutionId = id.ToString(),
                Outcome = PipelineOutcome.Completed,
                Duration = TimeSpan.Zero,
            };
        }
    }

    private static ScheduledPipelineDispatcher NewDispatcher(IPipelineEngine engine)
    {
        ServiceCollection services = new();
        services.AddSingleton(engine);
        return new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ScheduledPipelineDispatcher>.Instance
        );
    }

    private static ScheduledPipelineRun Run(Guid channel, Guid pipeline) =>
        new(
            Guid.CreateVersion7(),
            "P",
            new()
            {
                BroadcasterId = channel,
                PipelineId = pipeline,
                TriggeredByUserId = "1",
                TriggeredByDisplayName = "A",
            }
        );

    private static async Task<ScheduledPipelineService> SeededSchedulerAsync(
        AuthDbContext db,
        ScheduledPipelineDispatcher dispatcher,
        TimeProvider clock,
        params (Guid Channel, Guid Pipeline)[] pipelines
    )
    {
        foreach ((Guid channel, Guid pipeline) in pipelines)
            db.Pipelines.Add(
                new()
                {
                    Id = pipeline,
                    BroadcasterId = channel,
                    Name = $"P-{pipeline}",
                    IsEnabled = true,
                    TriggerKind = "manual",
                }
            );
        await db.SaveChangesAsync();
        return new(db, dispatcher, clock, NullLogger<ScheduledPipelineService>.Instance);
    }

    private static Task ScheduleAsync(
        ScheduledPipelineService sut,
        Guid channel,
        Guid pipeline,
        int delaySeconds
    ) =>
        sut.ScheduleAsync(
            channel,
            pipeline,
            delaySeconds,
            new Dictionary<string, string>(),
            "1",
            "A"
        );

    [Fact]
    public async Task A_blocked_run_of_one_channel_does_not_stop_another_channels_run_from_reaching_the_engine()
    {
        Guid channelA = Guid.CreateVersion7();
        Guid channelB = Guid.CreateVersion7();
        Guid pipelineA = Guid.CreateVersion7();
        Guid pipelineB = Guid.CreateVersion7();
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = await SeededSchedulerAsync(
            AuthTestBuilder.NewContext(),
            dispatcher,
            clock,
            (channelA, pipelineA),
            (channelB, pipelineB)
        );
        await ScheduleAsync(sut, channelA, pipelineA, 10);
        await ScheduleAsync(sut, channelB, pipelineB, 11);
        clock.Advance(TimeSpan.FromSeconds(20));

        int fired = await sut.FireDueAsync().WaitAsync(Limit);

        fired.Should().Be(2);
        await TestWait.UntilAsync(
            () => fake.Started(pipelineB),
            "channel B's run to reach the engine"
        );
        fake.Started(pipelineA).Should().BeTrue();
        fake.Timeline.Should()
            .NotContain($"end:{pipelineA}", "channel A's run is still blocked inside the engine");

        fake.Release(pipelineA);
        fake.Release(pipelineB);
        await dispatcher.DrainAsync(new CancellationTokenSource(Limit).Token);
    }

    [Fact]
    public async Task The_next_sweep_returns_while_a_run_still_blocks_and_its_own_new_run_reaches_the_engine()
    {
        Guid channelA = Guid.CreateVersion7();
        Guid channelC = Guid.CreateVersion7();
        Guid pipelineA = Guid.CreateVersion7();
        Guid pipelineC = Guid.CreateVersion7();
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        Microsoft.Extensions.Time.Testing.FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = await SeededSchedulerAsync(
            AuthTestBuilder.NewContext(),
            dispatcher,
            clock,
            (channelA, pipelineA),
            (channelC, pipelineC)
        );
        await ScheduleAsync(sut, channelA, pipelineA, 5);
        clock.Advance(TimeSpan.FromSeconds(6));
        (await sut.FireDueAsync().WaitAsync(Limit)).Should().Be(1);
        await TestWait.UntilAsync(
            () => fake.Started(pipelineA),
            "channel A's run to block in the engine"
        );

        await ScheduleAsync(sut, channelC, pipelineC, 5);
        clock.Advance(TimeSpan.FromSeconds(6));
        int second = await sut.FireDueAsync().WaitAsync(Limit);

        second.Should().Be(1, "the second sweep must finish although channel A's run still blocks");
        await TestWait.UntilAsync(
            () => fake.Started(pipelineC),
            "channel C's run to reach the engine"
        );
        fake.Timeline.Should().NotContain($"end:{pipelineA}");

        fake.Release(pipelineA);
        fake.Release(pipelineC);
        await dispatcher.DrainAsync(new CancellationTokenSource(Limit).Token);
    }

    [Fact]
    public async Task Two_runs_of_one_channel_run_in_due_order_and_never_at_the_same_time()
    {
        Guid channel = Guid.CreateVersion7();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        dispatcher.Enqueue(Run(channel, first));
        dispatcher.Enqueue(Run(channel, second));

        await TestWait.UntilAsync(() => fake.Started(first), "the first run to start");
        await Task.Delay(Settle, CancellationToken.None);
        fake.Started(second).Should().BeFalse("the second run waits for the first of its channel");

        fake.Release(first);
        await TestWait.UntilAsync(
            () => fake.Started(second),
            "the second run to start after the first ended"
        );
        fake.Release(second);
        await dispatcher.DrainAsync(new CancellationTokenSource(Limit).Token);

        fake.Timeline.Should()
            .Equal($"start:{first}", $"end:{first}", $"start:{second}", $"end:{second}");
        fake.MaxInside.Should().Be(1);
    }

    [Fact]
    public async Task No_more_than_the_run_bound_are_inside_the_engine_and_a_freed_slot_admits_the_next_run()
    {
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        int total = ScheduledPipelineDispatcher.MaxConcurrentRuns + 2;
        List<Guid> pipelines = [.. Enumerable.Range(0, total).Select(_ => Guid.CreateVersion7())];
        foreach (Guid pipeline in pipelines)
            dispatcher.Enqueue(Run(Guid.CreateVersion7(), pipeline));

        await TestWait.UntilAsync(
            () => fake.StartedCount >= ScheduledPipelineDispatcher.MaxConcurrentRuns,
            "the first runs to fill every slot"
        );
        await Task.Delay(Settle, CancellationToken.None);
        fake.StartedCount.Should().Be(ScheduledPipelineDispatcher.MaxConcurrentRuns);
        fake.MaxInside.Should().Be(ScheduledPipelineDispatcher.MaxConcurrentRuns);

        Guid firstStarted = pipelines.First(fake.Started);
        fake.Release(firstStarted);
        await TestWait.UntilAsync(
            () => fake.StartedCount >= ScheduledPipelineDispatcher.MaxConcurrentRuns + 1,
            "a freed slot to admit the next run"
        );

        foreach (Guid pipeline in pipelines)
            fake.Release(pipeline);
        await dispatcher.DrainAsync(new CancellationTokenSource(Limit).Token);
        fake.StartedCount.Should().Be(total);
        fake.MaxInside.Should().Be(ScheduledPipelineDispatcher.MaxConcurrentRuns);
    }

    [Fact]
    public async Task Shutdown_waits_for_a_running_run_and_cancels_nothing_when_it_finishes_in_time()
    {
        Guid channel = Guid.CreateVersion7();
        Guid pipeline = Guid.CreateVersion7();
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        TimerCountingTimeProvider clock = new(Start);
        ScheduledPipelineExpiryService sut = NewExpiryService(dispatcher, clock);
        dispatcher.Enqueue(Run(channel, pipeline));
        await TestWait.UntilAsync(() => fake.Started(pipeline), "the run to start");

        Task stop = sut.StopAsync(CancellationToken.None);
        await clock.WaitForTimersAsync(1);
        stop.IsCompleted.Should().BeFalse("shutdown waits for the in-flight run");

        fake.Release(pipeline);
        await stop.WaitAsync(Limit);

        fake.Timeline.Should().Equal($"start:{pipeline}", $"end:{pipeline}");
        fake.Cancelled.Should().Be(0);
    }

    [Fact]
    public async Task Shutdown_cancels_a_run_still_going_after_the_drain_timeout_on_the_fake_clock()
    {
        Guid channel = Guid.CreateVersion7();
        Guid pipeline = Guid.CreateVersion7();
        BlockingEngine fake = new();
        ScheduledPipelineDispatcher dispatcher = NewDispatcher(fake.Engine);
        TimerCountingTimeProvider clock = new(Start);
        ScheduledPipelineExpiryService sut = NewExpiryService(dispatcher, clock);
        dispatcher.Enqueue(Run(channel, pipeline));
        await TestWait.UntilAsync(() => fake.Started(pipeline), "the run to start");

        Task stop = sut.StopAsync(CancellationToken.None);
        await clock.WaitForTimersAsync(1);
        clock.Advance(ScheduledPipelineExpiryService.DrainTimeout - TimeSpan.FromSeconds(1));
        await Task.Delay(Settle, CancellationToken.None);
        stop.IsCompleted.Should().BeFalse("the drain timeout has not passed yet");
        fake.Cancelled.Should().Be(0);

        clock.Advance(TimeSpan.FromSeconds(2));
        await stop.WaitAsync(Limit);

        await TestWait.UntilAsync(() => fake.Cancelled == 1, "the run's token to be cancelled");
        fake.Timeline.Should().Equal($"start:{pipeline}", $"end:{pipeline}");
    }

    private static ScheduledPipelineExpiryService NewExpiryService(
        IScheduledPipelineDispatcher dispatcher,
        TimeProvider clock
    ) =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            dispatcher,
            clock,
            NullLogger<ScheduledPipelineExpiryService>.Instance
        );
}
