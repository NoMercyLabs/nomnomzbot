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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
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
        IPipelineEngine Engine
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        IChatProvider chat = Substitute.For<IChatProvider>();
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
            .BuildServiceProvider();

        TimerService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            templates,
            new FakeTimeProvider(Now),
            NullLogger<TimerService>.Instance
        );

        return new Harness(service, db, chat, engine);
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
            new Pipeline
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
                    && r.PipelineJson!.Contains("shoutout")
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
}
