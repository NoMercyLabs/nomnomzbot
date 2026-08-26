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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d3c: proves the wall-clock half of <c>wait_for_event</c> is actually FIRED by a hosted
/// worker rather than only being independently reachable-in-theory — <see cref="WaitForEventTimeoutSweepWorker"/>'s
/// internal <c>SweepAsync</c> seam (mirrors <c>GiveawayClaimSweepWorker</c>/<c>WebhookDeliveryWorker</c>)
/// calls the REAL <see cref="IPipelineEngine.ResumeTimedOutWaitsAsync"/> and resumes a run whose deadline has
/// elapsed, and — like its siblings — is a clean no-op when another instance already holds the sweep lease
/// (multi-instance deploy overlap must never double-resume the same run).
/// </summary>
public sealed class WaitForEventTimeoutSweepWorkerTests
{
    private static readonly Guid Channel = Guid.Parse("019f4b00-2222-7000-8000-000000000c01");

    private static (
        WaitForEventTimeoutSweepWorker Worker,
        IServiceScopeFactory ScopeFactory
    ) BuildWorker(
        PipelineTreeExecutionTestDbContext db,
        FakeTimeProvider clock,
        IRunOnceGuard guard
    )
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

        IPipelineEngine engine = new PipelineEngine(
            db,
            registry,
            [new StopAction(), new SetVariableAction(), new WaitForEventAction(resolver)],
            [],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            clock
        );

        ServiceCollection services = new();
        services.AddSingleton(engine);
        services.AddSingleton(guard);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return (
            new(scopeFactory, clock, Substitute.For<ILogger<WaitForEventTimeoutSweepWorker>>()),
            scopeFactory
        );
    }

    private static (PipelineStep Wrapper, PipelineStep Wait) BuildWaitPipeline(
        Guid pipelineId,
        int timeoutSeconds
    )
    {
        PipelineStep wrapper = new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = Channel,
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
            BroadcasterId = Channel,
            ParentStepId = wrapper.Id,
            Order = 0,
            ActionType = "wait_for_event",
            ConfigJson =
                $$"""{"type":"wait_for_event","event_name":"never_arrives","timeout_seconds":{{timeoutSeconds}}}""",
            IsEnabled = true,
        };
        return (wrapper, wait);
    }

    [Fact]
    public async Task SweepAsync_DeadlineElapsed_ResumesTheRunDownTheTimeoutPath()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();
        (PipelineStep wrapper, PipelineStep wait) = BuildWaitPipeline(
            pipelineId,
            timeoutSeconds: 5
        );
        db.PipelineSteps.AddRange(wrapper, wait);
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));
        (WaitForEventTimeoutSweepWorker worker, IServiceScopeFactory scopeFactory) = BuildWorker(
            db,
            clock,
            new SharedFakeRunOnceGuard()
        );

        // Suspend the run at t=0 through the worker's own engine instance.
        using IServiceScope scope = scopeFactory.CreateScope();
        IPipelineEngine engine = scope.ServiceProvider.GetRequiredService<IPipelineEngine>();
        PipelineExecutionResult first = await engine.ExecuteAsync(
            new()
            {
                BroadcasterId = Channel,
                PipelineId = pipelineId,
                TriggeredByUserId = "019f4b00-2222-7000-8000-000000000ccc",
                TriggeredByDisplayName = "TestUser",
            }
        );
        first.Outcome.Should().Be(PipelineOutcome.Suspended);

        // Advance past the 5s deadline, then let the worker's sweep seam fire.
        clock.Advance(TimeSpan.FromSeconds(6));
        await worker.SweepAsync(CancellationToken.None);

        PipelineRunState resumed = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        resumed.Status.Should().Be("completed");
    }

    [Fact]
    public async Task SweepAsync_AnotherInstanceHoldsTheLease_IsACleanNoOp()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> sharedLeaseStore = new();
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);

        // Instance A already holds the lease this tick.
        IAsyncDisposable? preHeld = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync(
            WaitForEventTimeoutSweepWorker.LeaseResourceName,
            TimeSpan.FromSeconds(30),
            CancellationToken.None
        );
        preHeld.Should().NotBeNull();

        (WaitForEventTimeoutSweepWorker worker, _) = BuildWorker(
            db,
            clock,
            new SharedFakeRunOnceGuard(sharedLeaseStore)
        );

        Func<Task> act = () => worker.SweepAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
