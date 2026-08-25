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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S008b: a pipeline run must PERSIST its outcome to <see cref="PipelineExecution"/> — before this slice the
/// table (schema H.4) had zero writers, so a misbehaving command left no execution history behind. Runs on a
/// real relational SQLite <see cref="EventStoreTestDbContext"/> (not EF InMemory, which does not support
/// <c>ExecuteDeleteAsync</c> — the retention sweep below relies on it).
/// </summary>
public sealed class PipelineExecutionPersistenceTests : IDisposable
{
    private static readonly Guid TestChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Open();

    public void Dispose() => _database.Dispose();

    private PipelineEngine CreateEngine(EventStoreTestDbContext db, FakeTimeProvider time)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);

        ICommandAction[] actions = [new StopAction(), new SetVariableAction()];
        ICommandCondition[] conditions = [];

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        return new(
            db,
            registry,
            actions,
            conditions,
            resolver,
            NullLogger<PipelineEngine>.Instance,
            time
        );
    }

    private static PipelineRequest BuildRequest(string json, Guid? pipelineId = null) =>
        new()
        {
            BroadcasterId = TestChannel,
            PipelineId = pipelineId,
            PipelineJson = json,
            TriggeredByUserId = Guid.NewGuid().ToString(),
            TriggeredByDisplayName = "viewer1",
        };

    [Fact]
    public async Task SuccessfulRun_PersistsCompletedRowWithStepLogs()
    {
        using EventStoreTestDbContext db = _database.NewContext();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T12:00:00Z"));
        PipelineEngine engine = CreateEngine(db, time);

        string json = /*lang=json*/
            """{"steps":[{"action":{"type":"set_variable","name":"x","value":"1"}}]}""";

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.Outcome.Should().Be(PipelineOutcome.Completed);

        using EventStoreTestDbContext verify = _database.NewContext();
        PipelineExecution row = await verify.PipelineExecutions.SingleAsync(e =>
            e.BroadcasterId == TestChannel
        );
        row.Status.Should().Be("completed");
        row.HostCallCount.Should().Be(1);
        row.ErrorMessage.Should().BeNull();
        row.StepLogsJson.Should().NotBeNullOrEmpty();
        row.StepLogsJson.Should().Contain("set_variable");
        row.StartedAt.Should().Be(time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task FailingStep_PersistsPartiallyFailedRowWithFailingStepIdentifiable()
    {
        using EventStoreTestDbContext db = _database.NewContext();
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-08-23T12:00:00Z"));
        PipelineEngine engine = CreateEngine(db, time);

        // "unknown_action" has no registered ICommandAction — fail-closed aborts the pipeline
        // (PipelineEngine.ExecuteActionAsync), producing a retrievable failing step.
        string json = /*lang=json*/
            """
            {"steps":[
                {"action":{"type":"set_variable","name":"x","value":"1"}},
                {"action":{"type":"unknown_action"}}
            ]}
            """;

        PipelineExecutionResult result = await engine.ExecuteAsync(BuildRequest(json));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);

        using EventStoreTestDbContext verify = _database.NewContext();
        PipelineExecution row = await verify.PipelineExecutions.SingleAsync(e =>
            e.BroadcasterId == TestChannel
        );
        row.Status.Should().Be("partially_failed");

        // The failing step must be identifiable from the persisted log, not just "something failed".
        row.StepLogsJson.Should().Contain("\"StepIndex\":1");
        row.StepLogsJson.Should().Contain("unknown_action");
        row.StepLogsJson.Should().Contain("Unknown action type");
    }

    [Fact]
    public async Task Retention_PurgesSuccessRowsPastTtl_ButKeepsFailureRowsLonger()
    {
        using EventStoreTestDbContext seedDb = _database.NewContext();
        DateTime now = DateTime.UtcNow;

        seedDb.PipelineExecutions.Add(
            new()
            {
                PipelineId = Guid.Empty,
                BroadcasterId = TestChannel,
                TriggerKind = "inline_json",
                Status = "completed",
                StartedAt = now - TimeSpan.FromDays(4), // past the 3-day success TTL
            }
        );
        seedDb.PipelineExecutions.Add(
            new()
            {
                PipelineId = Guid.Empty,
                BroadcasterId = TestChannel,
                TriggerKind = "inline_json",
                Status = "failed",
                StartedAt = now - TimeSpan.FromDays(4), // well inside the 30-day failure TTL
            }
        );
        await seedDb.SaveChangesAsync();

        using EventStoreTestDbContext db = _database.NewContext();
        FakeTimeProvider time = new(now);
        PipelineEngine engine = CreateEngine(db, time);

        // Any successful run triggers the channel's retention sweep as a side effect.
        await engine.ExecuteAsync(
            BuildRequest( /*lang=json*/
                """{"steps":[{"action":{"type":"stop","parameters":{}}}]}"""
            )
        );

        using EventStoreTestDbContext verify = _database.NewContext();
        List<PipelineExecution> remaining = await verify
            .PipelineExecutions.Where(e => e.BroadcasterId == TestChannel)
            .ToListAsync();

        remaining
            .Should()
            .NotContain(e => e.Status == "completed" && e.StartedAt < now - TimeSpan.FromDays(3));
        remaining.Should().Contain(e => e.Status == "failed");
    }

    [Fact]
    public async Task Retention_CapsRowCountPerChannelEvenInsideTtl()
    {
        using EventStoreTestDbContext seedDb = _database.NewContext();
        DateTime now = DateTime.UtcNow;

        // 501 fresh failure rows (all inside the 30-day TTL) — one more than the 500-row hard cap.
        for (int i = 0; i < 501; i++)
        {
            seedDb.PipelineExecutions.Add(
                new()
                {
                    PipelineId = Guid.Empty,
                    BroadcasterId = TestChannel,
                    TriggerKind = "inline_json",
                    Status = "failed",
                    StartedAt = now - TimeSpan.FromMinutes(501 - i),
                }
            );
        }
        await seedDb.SaveChangesAsync();

        using EventStoreTestDbContext db = _database.NewContext();
        FakeTimeProvider time = new(now);
        PipelineEngine engine = CreateEngine(db, time);

        await engine.ExecuteAsync(
            BuildRequest( /*lang=json*/
                """{"steps":[{"action":{"type":"stop","parameters":{}}}]}"""
            )
        );

        using EventStoreTestDbContext verify = _database.NewContext();
        int total = await verify
            .PipelineExecutions.Where(e => e.BroadcasterId == TestChannel)
            .CountAsync();

        // 501 seeded + 1 just-persisted run = 502, capped down to the 500-row ceiling.
        total.Should().Be(500);
    }
}
