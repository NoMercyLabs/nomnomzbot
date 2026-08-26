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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Obs.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Obs.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d3c: proves <c>wait_for_event</c> actually wakes on the LIVE bot, not just when a test
/// calls <see cref="IPipelineEngine.ResumeSuspendedRunsForEventAsync"/> directly. A real domain event
/// (<see cref="ObsEventReceivedEvent"/>) goes through the REAL <see cref="EventBus"/>, which resolves
/// the REAL <see cref="ObsEventTriggerSource"/> handler from a real DI container — the same handler that
/// dispatches to <see cref="EventResponseExecutor"/> for every OTHER trigger source — and it is
/// <see cref="EventResponseExecutor"/> alone that now also resumes matching suspended waits (the fix).
/// No test in this file calls <c>ResumeSuspendedRunsForEventAsync</c> or
/// <c>ResumeTimedOutWaitsAsync</c> by hand.
/// </summary>
public sealed class WaitForEventProductionWiringTests
{
    private static readonly Guid ChannelA = Guid.Parse("019f4a00-1111-7000-8000-0000000000a1");
    private static readonly Guid ChannelB = Guid.Parse("019f4a00-1111-7000-8000-0000000000b2");

    /// <summary>Same recording fixture as <c>WaitForEventActionTests</c> — records what the step right
    /// after the wait sees, keyed by broadcaster so two channels' runs don't collide in one sink.</summary>
    private sealed class RecordEventVarsAction(List<(Guid Channel, string Line)> sink)
        : ICommandAction
    {
        public string ActionType => "record_event_vars";
        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            sink.Add(
                (
                    ctx.BroadcasterId,
                    string.Join(
                        "|",
                        ctx.Variables.GetValueOrDefault("event.name", "-"),
                        ctx.Variables.GetValueOrDefault("event.matched", "-"),
                        ctx.Variables.GetValueOrDefault("event.obs.event.type", "-")
                    )
                )
            );
            return Task.FromResult(ActionResult.Success());
        }
    }

    private static (PipelineStep Wrapper, PipelineStep Wait, PipelineStep Record) BuildWaitPipeline(
        Guid channel,
        Guid pipelineId,
        string eventName
    )
    {
        PipelineStep wrapper = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = channel,
            BlockKind = "loop",
            BlockConfigJson = """{"mode":"repeat","count":1}""",
            Order = 0,
            ActionType = "noop",
            ConfigJson = """{"type":"noop"}""",
            IsEnabled = true,
        };
        PipelineStep wait = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = channel,
            ParentStepId = wrapper.Id,
            Order = 0,
            ActionType = "wait_for_event",
            ConfigJson = $$"""{"type":"wait_for_event","event_name":"{{eventName}}"}""",
            IsEnabled = true,
        };
        PipelineStep record = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = channel,
            ParentStepId = wrapper.Id,
            Order = 1,
            ActionType = "record_event_vars",
            ConfigJson = """{"type":"record_event_vars"}""",
            IsEnabled = true,
        };
        return (wrapper, wait, record);
    }

    private static async Task<(
        IEventBus Bus,
        PipelineTreeExecutionTestDbContext Db,
        List<(Guid Channel, string Line)> Recorded,
        Guid PipelineAId,
        Guid PipelineBId
    )> BuildAsync()
    {
        PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        List<(Guid, string)> recorded = [];

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(registry);
        services.AddSingleton(resolver);
        services.AddSingleton<IChatProvider>(Substitute.For<IChatProvider>());
        services.AddSingleton(Substitute.For<IEventResponseOverlayNotifier>());
        services.AddSingleton<ICommandAction>(new StopAction());
        services.AddSingleton<ICommandAction>(new SetVariableAction());
        services.AddSingleton<ICommandAction>(new WaitForEventAction(resolver));
        services.AddSingleton<ICommandAction>(new RecordEventVarsAction(recorded));
        services.AddSingleton<IEnumerable<ICommandCondition>>([]);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IPipelineEngine, PipelineEngine>();
        services.AddSingleton<IEventResponseExecutor, EventResponseExecutor>();
        services.AddSingleton<EventLogger>();
        services.AddSingleton<IEventBus, EventBus>();

        // THE handler under test — resolved by the scan-discovered convention in production
        // (AddOpenGenericHandlers over IEventHandler<>); wired explicitly here since this is a hand-
        // rolled container, not the full app composition root (that DI wiring is proven separately
        // by AssemblyScanDiscoveryTests' generic IEventHandler<> + BackgroundService coverage, which
        // now also sweeps in WaitForEventTimeoutSweepWorker without any list to hand-maintain).
        services.AddSingleton<IEventHandler<ObsEventReceivedEvent>, ObsEventTriggerSource>();

        ServiceProvider provider = services.BuildServiceProvider();
        IEventBus bus = provider.GetRequiredService<IEventBus>();

        Guid pipelineAId = Guid.NewGuid();
        Guid pipelineBId = Guid.NewGuid();
        (PipelineStep wA, PipelineStep waitA, PipelineStep rA) = BuildWaitPipeline(
            ChannelA,
            pipelineAId,
            "obs.SceneChanged"
        );
        (PipelineStep wB, PipelineStep waitB, PipelineStep rB) = BuildWaitPipeline(
            ChannelB,
            pipelineBId,
            "obs.SceneChanged"
        );
        db.PipelineSteps.AddRange(wA, waitA, rA, wB, waitB, rB);
        await db.SaveChangesAsync();

        return (bus, db, recorded, pipelineAId, pipelineBId);
    }

    private static PipelineRequest BuildRequest(Guid channel, Guid pipelineId) =>
        new()
        {
            BroadcasterId = channel,
            PipelineId = pipelineId,
            TriggeredByUserId = "019f4a00-1111-7000-8000-00000000cccc",
            TriggeredByDisplayName = "TestUser",
        };

    [Fact]
    public async Task PublishingObsEventOnTheRealBus_ResumesTheMatchingSuspendedRun_WithEventDataReadable()
    {
        (
            IEventBus bus,
            PipelineTreeExecutionTestDbContext db,
            List<(Guid, string)> recorded,
            Guid pipelineAId,
            _
        ) = await BuildAsync();

        // Suspend channel A's run first via a direct ExecuteAsync — this is the pipeline's OWN
        // entrypoint, not the resume seam under test.
        IPipelineEngine directEngine = BuildDirectEngine(db);
        PipelineExecutionResult first = await directEngine.ExecuteAsync(
            BuildRequest(ChannelA, pipelineAId)
        );
        first.Outcome.Should().Be(PipelineOutcome.Suspended);
        recorded.Should().BeEmpty();

        // The REAL bus publish — production's only entry point for a live event.
        await bus.PublishAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelA,
                ObsEventType = "SceneChanged",
                DataJson = """{"sceneName":"Gameplay"}""",
            }
        );

        recorded.Should().ContainSingle(r => r.Item1 == ChannelA);
        recorded
            .Single(r => r.Item1 == ChannelA)
            .Item2.Should()
            .Be("obs.SceneChanged|true|SceneChanged");

        PipelineRunState completed = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        completed.Status.Should().Be("completed");
    }

    [Fact]
    public async Task PublishingObsEventForChannelA_NeverResumesChannelBsSuspendedRun_TenantScoped()
    {
        (
            IEventBus bus,
            PipelineTreeExecutionTestDbContext db,
            List<(Guid, string)> recorded,
            Guid pipelineAId,
            Guid pipelineBId
        ) = await BuildAsync();

        IPipelineEngine directEngine = BuildDirectEngine(db);
        PipelineExecutionResult runA = await directEngine.ExecuteAsync(
            BuildRequest(ChannelA, pipelineAId)
        );
        PipelineExecutionResult runB = await directEngine.ExecuteAsync(
            BuildRequest(ChannelB, pipelineBId)
        );
        runA.Outcome.Should().Be(PipelineOutcome.Suspended);
        runB.Outcome.Should().Be(PipelineOutcome.Suspended);

        await bus.PublishAsync(
            new ObsEventReceivedEvent
            {
                BroadcasterId = ChannelA,
                ObsEventType = "SceneChanged",
                DataJson = "{}",
            }
        );

        recorded.Should().ContainSingle(); // only channel A's run advanced
        recorded.Single().Item1.Should().Be(ChannelA);

        PipelineRunState stillWaitingB = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == runB.SuspendedRunStateId!.Value
        );
        stillWaitingB.Status.Should().Be("suspended");
    }

    /// <summary>A second, isolated engine sharing the same db — mirrors resuming from a fresh process
    /// (a bus-delivered event never runs on the same in-memory engine instance that created the
    /// suspension in a real deployment).</summary>
    private static IPipelineEngine BuildDirectEngine(PipelineTreeExecutionTestDbContext db)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));
        return new PipelineEngine(
            db,
            registry,
            [new StopAction(), new SetVariableAction(), new WaitForEventAction(resolver)],
            [],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            TimeProvider.System
        );
    }
}
