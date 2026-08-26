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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-PIPE-TREE-d3b: the <c>wait_for_event</c> action itself — event matching, timeout, and
/// stream-offline cancellation, built on the S-PIPE-TREE-d3a persistence core.
/// </summary>
public sealed class WaitForEventActionTests
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");

    /// <summary>Appends the current <c>event.name</c>/<c>event.matched</c>/<c>event.timed_out</c>/
    /// <c>event.payload</c> variables to a shared sink every time it runs — placed right AFTER the
    /// <c>wait_for_event</c> step so a test can assert exactly what a later step sees post-resume.</summary>
    private sealed class RecordEventVarsAction(List<string> sink) : ICommandAction
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
                string.Join(
                    "|",
                    ctx.Variables.GetValueOrDefault("event.name", "-"),
                    ctx.Variables.GetValueOrDefault("event.matched", "-"),
                    ctx.Variables.GetValueOrDefault("event.timed_out", "-"),
                    ctx.Variables.GetValueOrDefault("event.payload", "-")
                )
            );
            return Task.FromResult(ActionResult.Success());
        }
    }

    private static PipelineEngine CreateEngine(
        NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext db,
        IEnumerable<ICommandAction> extraActions,
        TimeProvider? timeProvider = null
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

        List<ICommandAction> actions =
        [
            new StopAction(),
            new SetVariableAction(),
            new WaitForEventAction(resolver),
            .. extraActions,
        ];
        ICommandCondition[] conditions = [];

        return new(
            db,
            registry,
            actions,
            conditions,
            resolver,
            NullLogger<PipelineEngine>.Instance,
            timeProvider ?? TimeProvider.System
        );
    }

    private static PipelineRequest BuildRequest(Guid pipelineId) =>
        new()
        {
            BroadcasterId = TestChannel,
            PipelineId = pipelineId,
            TriggeredByUserId = "0192a000-0000-7000-8000-00000000abcd",
            TriggeredByDisplayName = "TestUser",
            MessageId = "msg1",
            RawMessage = "",
        };

    private static PipelineStep NewStep(
        Guid pipelineId,
        Guid? parentStepId,
        string? blockKind,
        string? blockConfigJson,
        int order,
        string actionType = "noop",
        string configJson = """{"type":"noop"}"""
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            BroadcasterId = TestChannel,
            ParentStepId = parentStepId,
            BlockKind = blockKind,
            BlockConfigJson = blockConfigJson,
            Order = order,
            ActionType = actionType,
            ConfigJson = configJson,
            IsEnabled = true,
        };

    /// <summary>Wraps the wait + a trailing record step inside a trivial <c>loop</c> block so the
    /// engine routes the run through the TREE walker (the only path that understands
    /// <c>ActionResult.Suspended</c>) — mirrors the wrapping pattern in the d3a persistence-core tests.</summary>
    private static (PipelineStep Wrapper, PipelineStep Wait, PipelineStep Record) BuildWaitPipeline(
        Guid pipelineId,
        string eventName,
        int? timeoutSeconds = null
    )
    {
        PipelineStep wrapper = NewStep(
            pipelineId,
            null,
            "loop",
            """{"mode":"repeat","count":1}""",
            0
        );
        string waitConfig = timeoutSeconds is int t
            ? $$"""{"type":"wait_for_event","event_name":"{{eventName}}","timeout_seconds":{{t}}}"""
            : $$"""{"type":"wait_for_event","event_name":"{{eventName}}"}""";
        PipelineStep wait = NewStep(
            pipelineId,
            wrapper.Id,
            null,
            null,
            0,
            "wait_for_event",
            waitConfig
        );
        PipelineStep record = NewStep(
            pipelineId,
            wrapper.Id,
            null,
            null,
            1,
            "record_event_vars",
            """{"type":"record_event_vars"}"""
        );
        return (wrapper, wait, record);
    }

    // ─── Matching event resumes with data ──────────────────────────────────────

    [Fact]
    public async Task ResumeSuspendedRunsForEventAsync_MatchingEventName_ResumesWithEventDataReadableByLaterStep()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        (PipelineStep wrapper, PipelineStep wait, PipelineStep record) = BuildWaitPipeline(
            pipelineId,
            "song_requested"
        );
        db.PipelineSteps.AddRange(wrapper, wait, record);
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)]);

        PipelineExecutionResult first = await engine.ExecuteAsync(BuildRequest(pipelineId));
        first.Outcome.Should().Be(PipelineOutcome.Suspended);
        recorded.Should().BeEmpty(); // the record step never ran — the wait suspended before it

        PipelineRunState persisted = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        persisted.WaitEventName.Should().Be("song_requested");
        persisted.WaitTimeoutAt.Should().NotBeNull();

        int resumedCount = await engine.ResumeSuspendedRunsForEventAsync(
            TestChannel,
            "song_requested",
            new Dictionary<string, string> { ["payload"] = "Never Gonna Give You Up" }
        );

        resumedCount.Should().Be(1);
        recorded.Should().ContainSingle();
        recorded[0].Should().Be("song_requested|true|false|Never Gonna Give You Up");

        PipelineRunState completed = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        completed.Status.Should().Be("completed");
    }

    // ─── Non-matching event does NOT resume ────────────────────────────────────

    [Fact]
    public async Task ResumeSuspendedRunsForEventAsync_NonMatchingEventName_NeverResumesTheRun()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        (PipelineStep wrapper, PipelineStep wait, PipelineStep record) = BuildWaitPipeline(
            pipelineId,
            "song_requested"
        );
        db.PipelineSteps.AddRange(wrapper, wait, record);
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)]);

        PipelineExecutionResult first = await engine.ExecuteAsync(BuildRequest(pipelineId));
        first.Outcome.Should().Be(PipelineOutcome.Suspended);

        // A DIFFERENT event fires for the same channel — must be a no-op for this waiter.
        int resumedCount = await engine.ResumeSuspendedRunsForEventAsync(
            TestChannel,
            "raid_started",
            new Dictionary<string, string> { ["from"] = "SomeOtherStreamer" }
        );

        resumedCount.Should().Be(0);
        recorded.Should().BeEmpty(); // the waiter never resumed, never ran the record step

        PipelineRunState stillSuspended = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        stillSuspended.Status.Should().Be("suspended");
        stillSuspended.WaitEventName.Should().Be("song_requested");
    }

    // ─── Timeout resumes down the honest timeout path ──────────────────────────

    [Fact]
    public async Task ResumeTimedOutWaitsAsync_DeadlineElapsed_ResumesTheRunWithTimedOutMarkedTrue()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        (PipelineStep wrapper, PipelineStep wait, PipelineStep record) = BuildWaitPipeline(
            pipelineId,
            "song_requested",
            timeoutSeconds: 30
        );
        db.PipelineSteps.AddRange(wrapper, wait, record);
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)], clock);

        PipelineExecutionResult first = await engine.ExecuteAsync(BuildRequest(pipelineId));
        first.Outcome.Should().Be(PipelineOutcome.Suspended);

        // Before the deadline: the sweep must find nothing.
        int tooEarly = await engine.ResumeTimedOutWaitsAsync();
        tooEarly.Should().Be(0);
        recorded.Should().BeEmpty();

        // Past the 30s deadline.
        clock.Advance(TimeSpan.FromSeconds(31));

        int resumedCount = await engine.ResumeTimedOutWaitsAsync();
        resumedCount.Should().Be(1);
        recorded.Should().ContainSingle();
        recorded[0].Should().Be("song_requested|false|true|-");

        PipelineRunState completed = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        completed.Status.Should().Be("completed");
    }

    // ─── Stream offline cancels a suspended wait, recorded not deleted ─────────

    [Fact]
    public async Task CancelAllForChannelAsync_SuspendedWaitForEvent_IsCancelledAndRecordedNotDeleted()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        (PipelineStep wrapper, PipelineStep wait, PipelineStep record) = BuildWaitPipeline(
            pipelineId,
            "song_requested"
        );
        db.PipelineSteps.AddRange(wrapper, wait, record);
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)]);

        PipelineExecutionResult first = await engine.ExecuteAsync(BuildRequest(pipelineId));
        first.Outcome.Should().Be(PipelineOutcome.Suspended);

        await engine.CancelAllForChannelAsync(TestChannel);

        PipelineRunState cancelled = await db.PipelineRunStates.SingleAsync(r =>
            r.Id == first.SuspendedRunStateId!.Value
        );
        cancelled.Status.Should().Be("cancelled");
        cancelled.CompletedAt.Should().NotBeNull();

        // Never resurrected by a later matching event — the run was stranded on an offline stream.
        int resumedCount = await engine.ResumeSuspendedRunsForEventAsync(
            TestChannel,
            "song_requested",
            new Dictionary<string, string>()
        );
        resumedCount.Should().Be(0);
        recorded.Should().BeEmpty();
    }

    // ─── Per-channel suspended-run cap still holds for wait_for_event ──────────

    [Fact]
    public async Task ExecuteAsync_WaitForEventAtSuspendedRunCap_FailsHonestlyInsteadOfParking()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        (PipelineStep wrapper, PipelineStep wait, PipelineStep record) = BuildWaitPipeline(
            pipelineId,
            "song_requested"
        );
        db.PipelineSteps.AddRange(wrapper, wait, record);

        for (int i = 0; i < 50; i++)
        {
            db.PipelineRunStates.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    BroadcasterId = TestChannel,
                    PipelineId = pipelineId,
                    Status = "suspended",
                    VariablesJson = "{}",
                    CursorJson = "[]",
                    TriggeredByUserId = Guid.NewGuid(),
                    TriggeredByDisplayName = "filler",
                }
            );
        }
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Failed);
        result.ErrorMessage.Should().Contain("suspended_run_cap_exceeded");
    }

    // ─── Flat pipelines are unaffected ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FlatPipelineNeverUsingWaitForEvent_StillCompletesThroughTheOriginalPath()
    {
        using PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        Guid pipelineId = Guid.NewGuid();

        PipelineStep leaf = NewStep(
            pipelineId,
            null,
            null,
            null,
            0,
            "record_event_vars",
            """{"type":"record_event_vars"}"""
        );
        db.PipelineSteps.Add(leaf);
        await db.SaveChangesAsync();

        List<string> recorded = [];
        PipelineEngine engine = CreateEngine(db, [new RecordEventVarsAction(recorded)]);
        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(pipelineId));

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        recorded.Should().ContainSingle();
        recorded[0].Should().Be("-|-|-|-"); // no wait ever ran — nothing suspended, nothing to persist
    }
}
