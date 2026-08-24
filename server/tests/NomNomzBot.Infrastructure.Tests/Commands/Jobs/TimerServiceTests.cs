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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using Timer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Commands.Jobs;

/// <summary>
/// Proves the timer's two dispatch legs (commands-pipelines.md §I.1): a message timer still sends the
/// next round-robin chat line, and a PIPELINE timer — previously specced but never implemented — executes
/// its bound pipeline with the current rotation entry riding as <c>{timer.message}</c> (the rotating
/// auto-shoutout substrate), advancing the shared rotation index and stamping <c>LastFiredAt</c>.
/// </summary>
public sealed class TimerServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c301");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000c302");
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 15, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        TimerService Service,
        AuthDbContext Db,
        IChatProvider Chat,
        IPipelineEngine Engine,
        IRunOnceGuard Guard,
        ILogger<TimerService> Logger
    );

    private static Harness Build(IRunOnceGuard? guard = null, ILogger<TimerService>? logger = null)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        // Default: this instance always holds the lease — matches every pre-existing test's
        // single-instance assumption. Multi-instance lease tests below pass their own pre-configured
        // guard, which must NOT be overwritten here.
        if (guard is null)
        {
            guard = Substitute.For<IRunOnceGuard>();
            guard
                .TryAcquireAsync(
                    Arg.Any<string>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ => Substitute.For<IAsyncDisposable>());
        }
        logger ??= NullLogger<TimerService>.Instance;

        IChatProvider chat = Substitute.For<IChatProvider>();
        // Real transport sends succeed by default (S008d): an unconfigured NSubstitute bool call
        // defaults to false, which would silently flip every "timer fired fine" test into a false
        // "send failed, don't advance" outcome now that FireMessageAsync threads the real chat-send bool.
        // Tests exercising a send failure override this explicitly.
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "x1",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                }
            );

        ChannelContext ctx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-42",
            ChannelName = "qtkitte",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.GetAll().Returns([ctx]);
        registry.Get(Channel).Returns(ctx);

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => callInfo.ArgAt<string>(0));

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton(chat)
            .AddSingleton(engine)
            .AddSingleton(guard)
            .BuildServiceProvider();

        TimerService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            templates,
            new FakeTimeProvider(Now),
            logger
        );

        return new(service, db, chat, engine, guard, logger);
    }

    private static Timer SeedTimer(
        AuthDbContext db,
        Guid? pipelineId,
        List<string> messages,
        int nextIndex = 0,
        bool fireOnce = false
    )
    {
        Timer timer = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Name = "auto-shoutout",
            Messages = messages,
            PipelineId = pipelineId,
            IntervalMinutes = 15,
            IsEnabled = true,
            FireOnce = fireOnce,
            NextMessageIndex = nextIndex,
        };
        db.Timers.Add(timer);
        db.SaveChanges();
        return timer;
    }

    private static void SeedPipeline(AuthDbContext db, string? graphJson)
    {
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "shoutout rotation",
                TriggerKind = "timer",
                GraphJsonCache = graphJson,
            }
        );
        db.SaveChanges();
    }

    [Fact]
    public async Task A_pipeline_timer_executes_the_bound_pipeline_with_the_rotation_entry()
    {
        Harness h = Build();
        SeedPipeline(h.Db, """{"actions":[{"type":"shoutout","user_id":"{timer.message}"}]}""");
        Timer timer = SeedTimer(h.Db, PipelineId, ["alice", "bob"], nextIndex: 0);

        await h.Service.TickAsync(CancellationToken.None);

        await h
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Channel
                    && r.PipelineJson.Contains("shoutout")
                    && r.InitialVariables["timer.message"] == "alice"
                    && r.InitialVariables["timer.name"] == "auto-shoutout"
                ),
                Arg.Any<CancellationToken>()
            );
        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);

        Timer persisted = h.Db.Timers.Single(t => t.Id == timer.Id);
        persisted.NextMessageIndex.Should().Be(1, "the rotation advanced to the next entry");
        persisted.LastFiredAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task The_rotation_wraps_around_the_curated_list()
    {
        Harness h = Build();
        SeedPipeline(h.Db, """{"actions":[]}""");
        Timer timer = SeedTimer(h.Db, PipelineId, ["alice", "bob"], nextIndex: 1);

        await h.Service.TickAsync(CancellationToken.None);

        await h
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.InitialVariables["timer.message"] == "bob"),
                Arg.Any<CancellationToken>()
            );
        h.Db.Timers.Single(t => t.Id == timer.Id).NextMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task A_message_timer_still_sends_the_next_chat_line()
    {
        Harness h = Build();
        Timer timer = SeedTimer(h.Db, pipelineId: null, ["hello chat!"]);

        await h.Service.TickAsync(CancellationToken.None);

        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "hello chat!", Arg.Any<CancellationToken>());
        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        h.Db.Timers.Single(t => t.Id == timer.Id).LastFiredAt.Should().Be(Now.UtcDateTime);
    }

    /// <summary>
    /// Z1 — zero-downtime deploys deliberately run two instances against one database; without a lease
    /// both would send every due timer's message, doubling it in chat. Two TimerService instances share
    /// one guard: exactly one message goes out.
    /// </summary>
    [Fact]
    public async Task Two_instances_sharing_one_lease_send_the_due_timer_exactly_once()
    {
        // "locked" is held across the entire overlap window — NOT auto-released when a single
        // TickAsync's `await using` disposes its lease. This models the real failure: two instances
        // ticking during the SAME overlap window must collide, not just two sequential ticks that
        // happen to run one after another.
        IRunOnceGuard sharedGuard = Substitute.For<IRunOnceGuard>();
        bool locked = false;
        sharedGuard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (locked)
                    return Task.FromResult<IAsyncDisposable?>(null);
                locked = true;
                IAsyncDisposable lease = Substitute.For<IAsyncDisposable>();
                lease.DisposeAsync().Returns(_ => ValueTask.CompletedTask); // release is manual below
                return Task.FromResult<IAsyncDisposable?>(lease);
            });

        Harness first = Build(guard: sharedGuard);
        Timer timer = SeedTimer(first.Db, pipelineId: null, ["hello chat!"]);

        // The second instance shares the guard AND the database row the first instance just wrote to —
        // build it against the same db.
        IChatProvider secondChat = Substitute.For<IChatProvider>();
        secondChat
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ChannelContext ctx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-42",
            ChannelName = "qtkitte",
        };
        IChannelRegistry secondRegistry = Substitute.For<IChannelRegistry>();
        secondRegistry.GetAll().Returns([ctx]);
        secondRegistry.Get(Channel).Returns(ctx);
        ITemplateResolver secondTemplates = Substitute.For<ITemplateResolver>();
        secondTemplates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => callInfo.ArgAt<string>(0));
        ServiceProvider secondProvider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(first.Db)
            .AddSingleton(secondChat)
            .AddSingleton(Substitute.For<IPipelineEngine>())
            .AddSingleton(sharedGuard)
            .BuildServiceProvider();
        FakeTimeProvider secondClock = new(Now);
        TimerService second = new(
            secondProvider.GetRequiredService<IServiceScopeFactory>(),
            secondRegistry,
            secondTemplates,
            secondClock,
            NullLogger<TimerService>.Instance
        );

        // Both instances tick the same due timer "concurrently" — the holder ticks first, while the
        // lock is still held.
        await first.Service.TickAsync(CancellationToken.None);
        await second.TickAsync(CancellationToken.None);

        await first
            .Chat.Received(1)
            .SendMessageAsync(Channel, "hello chat!", Arg.Any<CancellationToken>());
        await secondChat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        first.Db.Timers.Single(t => t.Id == timer.Id).LastFiredAt.Should().Be(Now.UtcDateTime);

        // After the holder releases the lock and the timer's interval elapses, the OTHER instance
        // picks it up on a later tick and sends.
        locked = false;
        secondClock.Advance(TimeSpan.FromMinutes(15));
        await second.TickAsync(CancellationToken.None);

        await secondChat
            .Received(1)
            .SendMessageAsync(Channel, "hello chat!", Arg.Any<CancellationToken>());
        first
            .Db.Timers.Single(t => t.Id == timer.Id)
            .LastFiredAt.Should()
            .Be(Now.UtcDateTime.AddMinutes(15));
    }

    /// <summary>Z1 — the non-holder must be a clean no-op: no send, and no error-level log every 30s (a
    /// deploy overlap is normal, not a fault).</summary>
    [Fact]
    public async Task An_instance_without_the_lease_does_nothing_and_logs_no_error()
    {
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        guard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(null));
        ILogger<TimerService> logger = Substitute.For<ILogger<TimerService>>();

        Harness h = Build(guard: guard, logger: logger);
        SeedTimer(h.Db, pipelineId: null, ["hello chat!"]);

        await h.Service.TickAsync(CancellationToken.None);

        await h.Chat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!, default);
        logger
            .DidNotReceive()
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );
    }

    [Fact]
    public async Task A_message_timer_whose_send_fails_does_not_stamp_last_fired_or_advance_rotation()
    {
        // S008d: FireMessageAsync used to hardcode `true` regardless of the chat-send result, so a
        // failed transport send still stamped LastFiredAt and advanced NextMessageIndex — the line was
        // silently dropped forever instead of retrying on the next tick. Observed directly: with the
        // stub below returning false, the pre-fix code (hardcoded `return true`) would have stamped
        // LastFiredAt to Now and advanced the index anyway.
        Harness h = Build();
        Timer timer = SeedTimer(h.Db, pipelineId: null, ["hello chat!", "second line"]);
        h.Chat.SendMessageAsync(Channel, "hello chat!", Arg.Any<CancellationToken>())
            .Returns(false);

        await h.Service.TickAsync(CancellationToken.None);

        Timer persisted = h.Db.Timers.Single(t => t.Id == timer.Id);
        persisted.LastFiredAt.Should().BeNull();
        persisted.NextMessageIndex.Should().Be(0);
    }

    [Fact]
    public async Task A_pipeline_timer_with_no_executable_graph_skips_but_still_stamps_last_fired()
    {
        // A broken binding must retry on the next interval — never in a 30-second error loop.
        Harness h = Build();
        SeedPipeline(h.Db, graphJson: null);
        Timer timer = SeedTimer(h.Db, PipelineId, ["alice"]);

        await h.Service.TickAsync(CancellationToken.None);

        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        h.Db.Timers.Single(t => t.Id == timer.Id).LastFiredAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_pipeline_timer_bound_to_a_disabled_pipeline_does_not_run_it()
    {
        // Pipeline.IsEnabled=false must stop the timer from dispatching it — same retry-next-interval
        // shape as the missing-graph case above, never an error loop.
        Harness h = Build();
        h.Db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "disabled rotation",
                TriggerKind = "timer",
                GraphJsonCache = """{"actions":[{"type":"shoutout"}]}""",
                IsEnabled = false,
            }
        );
        h.Db.SaveChanges();
        Timer timer = SeedTimer(h.Db, PipelineId, ["alice"]);

        await h.Service.TickAsync(CancellationToken.None);

        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        h.Db.Timers.Single(t => t.Id == timer.Id).LastFiredAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_one_shot_timer_fires_once_then_disables_itself()
    {
        // FireOnce = a single dispatch: the line still goes out, but the timer disables itself so the next
        // tick skips it — the whole point of "trigger just once" instead of looping on the interval.
        Harness h = Build();
        Timer timer = SeedTimer(h.Db, pipelineId: null, ["one and done"], fireOnce: true);

        await h.Service.TickAsync(CancellationToken.None);

        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "one and done", Arg.Any<CancellationToken>());
        Timer persisted = h.Db.Timers.Single(t => t.Id == timer.Id);
        persisted
            .IsEnabled.Should()
            .BeFalse("a one-shot timer disables itself after its single fire");
        persisted.LastFiredAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_looping_timer_stays_enabled_after_firing()
    {
        // The default (FireOnce = false) must be untouched — it keeps looping, so it stays enabled.
        Harness h = Build();
        Timer timer = SeedTimer(h.Db, pipelineId: null, ["again and again"], fireOnce: false);

        await h.Service.TickAsync(CancellationToken.None);

        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "again and again", Arg.Any<CancellationToken>());
        h.Db.Timers.Single(t => t.Id == timer.Id)
            .IsEnabled.Should()
            .BeTrue("a looping timer stays enabled to fire again next interval");
    }

    [Fact]
    public async Task A_timer_that_is_not_due_yet_does_nothing()
    {
        Harness h = Build();
        SeedPipeline(h.Db, """{"actions":[]}""");
        Timer timer = SeedTimer(h.Db, PipelineId, ["alice"]);
        timer.LastFiredAt = Now.UtcDateTime.AddMinutes(-5); // interval is 15
        h.Db.SaveChanges();

        await h.Service.TickAsync(CancellationToken.None);

        await h.Engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        h.Db.Timers.Single(t => t.Id == timer.Id).NextMessageIndex.Should().Be(0);
    }

    /// <summary>
    /// S037 — a throwing tick used to skip its inter-tick delay (the delay sat INSIDE the try, so an
    /// exception jumped straight past it), spinning the loop hot. The delay now runs whether the tick
    /// threw or not: a second tick attempt must NOT happen until a full <c>TickInterval</c> has elapsed on
    /// the (fake, controllable) clock.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTickThrows_StillWaitsTheFullIntervalBeforeRetrying()
    {
        FakeTimeProvider clock = new(Now);
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.GetAll().Returns(_ => throw new InvalidOperationException("registry boom"));

        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        TimerService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            Substitute.For<ITemplateResolver>(),
            clock,
            NullLogger<TimerService>.Instance
        );

        using CancellationTokenSource cts = new();
        await service.StartAsync(cts.Token);
        try
        {
            // Let the background loop's first (throwing) tick run and reach its delay.
            await WaitUntilAsync(() => registry.ReceivedCalls().Count() >= 1);
            registry.ReceivedCalls().Count().Should().Be(1);

            // Advancing LESS than the 30s interval must not release the delay — no second tick yet.
            clock.Advance(TimeSpan.FromSeconds(29));
            await Task.Delay(50, CancellationToken.None);
            registry
                .ReceivedCalls()
                .Count()
                .Should()
                .Be(1, "the failing tick's delay has not elapsed yet");

            // Crossing the interval releases the delay and the loop retries exactly once more. The
            // advance is applied in one-second steps until the retry lands: the background loop
            // registers its delay on a real thread-pool turn, so under load it can still be BEFORE that
            // registration when the first advance fires — a single jump would then move the clock past
            // a timer that did not exist yet and the retry would never come. Stepping re-applies the
            // move until the timer exists, which is deterministic regardless of who wins that race.
            for (int step = 0; step < 60 && registry.ReceivedCalls().Count() < 2; step++)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                await WaitUntilAsync(() => registry.ReceivedCalls().Count() >= 2, iterations: 20);
            }

            registry.ReceivedCalls().Count().Should().Be(2);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Polls until the background loop has caught up. The budget is generous on purpose: this
    /// waits on a REAL thread-pool turn (only the clock is fake), so a machine running the full suite in
    /// parallel can easily need more than a second — a tighter budget made this test flake red in CI
    /// while passing in isolation. A condition that is genuinely never met still fails, just later.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int iterations = 200)
    {
        for (int i = 0; i < iterations && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }
}
