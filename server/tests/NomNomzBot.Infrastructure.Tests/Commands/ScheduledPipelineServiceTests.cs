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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the deferred-pipeline scheduler (the "run pipeline P once, T seconds from now" primitive): scheduling
/// persists an exact pending row (clock-driven due time, variables round-tripped), a dedupe key replaces the live
/// pending run instead of stacking, cancels transition state, delay is clamped, tenants are isolated, and firing
/// dispatches through the pipeline engine with the saved payload (never twice, and expiring anything too stale).
/// </summary>
public sealed class ScheduledPipelineServiceTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192b000-0000-7000-8000-00000000a001");
    private static readonly Guid ChannelB = Guid.Parse("0192b000-0000-7000-8000-00000000b001");
    private static readonly Guid PipelineId = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AuthDbContext> SeedPipelineAsync(
        Guid channel,
        Guid pipelineId,
        string name,
        AuthDbContext? existing = null,
        bool enabled = true
    )
    {
        AuthDbContext db = existing ?? AuthTestBuilder.NewContext();
        db.Pipelines.Add(
            new PipelineEntity
            {
                Id = pipelineId,
                BroadcasterId = channel,
                Name = name,
                IsEnabled = enabled,
                TriggerKind = "manual",
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static ScheduledPipelineService Build(
        AuthDbContext db,
        FakeTimeProvider clock,
        IPipelineEngine? engine = null
    )
    {
        // The service resolves the engine on a fresh scope at fire time; register it so that scope can hand it back.
        ServiceCollection services = new();
        services.AddSingleton(engine ?? Substitute.For<IPipelineEngine>());
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new(db, scopeFactory, clock, NullLogger<ScheduledPipelineService>.Instance);
    }

    private static IReadOnlyDictionary<string, string> Vars(params (string, string)[] kv) =>
        kv.ToDictionary(t => t.Item1, t => t.Item2);

    // ─── ScheduleAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleAsync_persists_a_pending_row_with_exact_due_time_and_round_tripped_variables()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Voice Swap Revert");
        FakeTimeProvider clock = new(Start);

        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleAsync(
                ChannelA,
                PipelineId,
                300,
                Vars(("revert.to", "en-US-Aria"), ("user.id", "555")),
                "555",
                "Bamo"
            );

        result.IsSuccess.Should().BeTrue();
        result.Value.PipelineId.Should().Be(PipelineId);
        result.Value.PipelineName.Should().Be("Voice Swap Revert");
        result.Value.Status.Should().Be(ScheduledPipelineTaskStatus.Pending);
        result.Value.DueAt.Should().Be(Start.AddSeconds(300));

        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.BroadcasterId.Should().Be(ChannelA);
        row.PipelineId.Should().Be(PipelineId);
        row.TriggeredByUserId.Should().Be("555");
        row.TriggeredByDisplayName.Should().Be("Bamo");
        row.DueAt.Should().Be(Start.AddSeconds(300));
        row.CreatedAt.Should().Be(Start);
        row.VariablesJson.Should().Contain("revert.to").And.Contain("en-US-Aria");
    }

    [Fact]
    public async Task ScheduleAsync_fails_not_found_for_a_pipeline_outside_the_tenant()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "A pipeline");
        FakeTimeProvider clock = new(Start);

        // Same pipeline id, but asking as channel B — the id belongs to A, so B must not schedule it.
        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleAsync(ChannelB, PipelineId, 60, Vars(), "1", "B");

        result.ErrorCode.Should().Be("NOT_FOUND");
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1)] // below the floor → clamped up to 1s
    [InlineData(-50, 1)]
    [InlineData(100_000, 86_400)] // above 24h → clamped down to 24h
    public async Task ScheduleAsync_clamps_the_delay_to_the_safe_range(
        int requested,
        int expectedSeconds
    )
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "P");
        FakeTimeProvider clock = new(Start);

        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleAsync(ChannelA, PipelineId, requested, Vars(), "1", "A");

        result.Value.DueAt.Should().Be(Start.AddSeconds(expectedSeconds));
    }

    // ─── ScheduleByNameAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleByNameAsync_resolves_the_name_case_insensitively_to_its_id()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Voice Swap Revert");
        FakeTimeProvider clock = new(Start);

        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleByNameAsync(ChannelA, "voice swap revert", 120, Vars(), "1", "A");

        result.IsSuccess.Should().BeTrue();
        result.Value.PipelineId.Should().Be(PipelineId);
        result.Value.PipelineName.Should().Be("Voice Swap Revert");
    }

    [Fact]
    public async Task ScheduleByNameAsync_fails_not_found_for_an_unknown_name_and_persists_nothing()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Real Pipeline");
        FakeTimeProvider clock = new(Start);

        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleByNameAsync(ChannelA, "Ghost Pipeline", 60, Vars(), "1", "A");

        result.ErrorCode.Should().Be("NOT_FOUND");
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ScheduleByNameAsync_will_not_resolve_a_disabled_pipeline()
    {
        AuthDbContext db = await SeedPipelineAsync(
            ChannelA,
            PipelineId,
            "Disabled",
            enabled: false
        );
        FakeTimeProvider clock = new(Start);

        Result<ScheduledPipelineTaskDto> result = await Build(db, clock)
            .ScheduleByNameAsync(ChannelA, "Disabled", 60, Vars(), "1", "A");

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ─── Dedupe ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_dedupe_key_replaces_the_pending_row_rather_than_stacking_a_second()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "P");
        FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = Build(db, clock);

        Result<ScheduledPipelineTaskDto> first = await sut.ScheduleAsync(
            ChannelA,
            PipelineId,
            60,
            Vars(("k", "first")),
            "1",
            "A",
            dedupeKey: "revert:555"
        );

        clock.Advance(TimeSpan.FromSeconds(5));
        Result<ScheduledPipelineTaskDto> second = await sut.ScheduleAsync(
            ChannelA,
            PipelineId,
            600,
            Vars(("k", "second")),
            "1",
            "A",
            dedupeKey: "revert:555"
        );

        // One row, same identity, re-anchored due time + payload.
        second.Value.Id.Should().Be(first.Value.Id);
        List<ScheduledPipelineTask> rows = await db.ScheduledPipelineTasks.ToListAsync();
        rows.Should().HaveCount(1);
        rows[0].DueAt.Should().Be(Start.AddSeconds(5 + 600));
        rows[0].VariablesJson.Should().Contain("second").And.NotContain("first");
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelAsync_marks_the_task_cancelled()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "P");
        FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = Build(db, clock);
        Result<ScheduledPipelineTaskDto> scheduled = await sut.ScheduleAsync(
            ChannelA,
            PipelineId,
            60,
            Vars(),
            "1",
            "A"
        );

        Result cancel = await sut.CancelAsync(ChannelA, scheduled.Value.Id);

        cancel.IsSuccess.Should().BeTrue();
        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.Status.Should().Be(ScheduledPipelineTaskStatus.Cancelled);
        (await sut.ListPendingAsync(ChannelA)).Should().BeEmpty();
    }

    [Fact]
    public async Task CancelByDedupeKeyAsync_cancels_the_matching_pending_task()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "P");
        FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = Build(db, clock);
        await sut.ScheduleAsync(ChannelA, PipelineId, 60, Vars(), "1", "A", dedupeKey: "revert:9");

        Result cancel = await sut.CancelByDedupeKeyAsync(ChannelA, "revert:9");

        cancel.IsSuccess.Should().BeTrue();
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Cancelled);
    }

    // ─── Tenant isolation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListPendingAsync_is_scoped_to_the_channel()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "P-A");
        Guid pipelineB = Guid.Parse("0192b000-0000-7000-8000-0000000000f2");
        await SeedPipelineAsync(ChannelB, pipelineB, "P-B", db);
        FakeTimeProvider clock = new(Start);
        ScheduledPipelineService sut = Build(db, clock);

        await sut.ScheduleAsync(ChannelA, PipelineId, 60, Vars(), "1", "A");
        await sut.ScheduleAsync(ChannelB, pipelineB, 60, Vars(), "1", "B");

        IReadOnlyList<ScheduledPipelineTaskDto> aPending = await sut.ListPendingAsync(ChannelA);
        aPending.Should().HaveCount(1);
        aPending[0].PipelineId.Should().Be(PipelineId);
    }

    // ─── FireDueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FireDueAsync_dispatches_a_past_due_task_with_its_saved_payload_and_marks_it_fired()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Revert");
        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        PipelineRequest? dispatched = null;
        engine
            .ExecuteAsync(
                Arg.Do<PipelineRequest>(r => dispatched = r),
                Arg.Any<CancellationToken>()
            )
            .Returns(Completed());
        ScheduledPipelineService sut = Build(db, clock, engine);

        await sut.ScheduleAsync(
            ChannelA,
            PipelineId,
            30,
            Vars(("revert.to", "en-US-Aria")),
            "555",
            "Bamo"
        );

        clock.Advance(TimeSpan.FromSeconds(31));
        int fired = await sut.FireDueAsync();

        fired.Should().Be(1);
        // The engine received the saved pipeline id, actor, and variables — the deferred run keeps its context.
        dispatched.Should().NotBeNull();
        dispatched!.BroadcasterId.Should().Be(ChannelA);
        dispatched.PipelineId.Should().Be(PipelineId);
        dispatched.TriggeredByUserId.Should().Be("555");
        dispatched.TriggeredByDisplayName.Should().Be("Bamo");
        dispatched.InitialVariables.Should().ContainKey("revert.to");
        dispatched.InitialVariables["revert.to"].Should().Be("en-US-Aria");

        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.Status.Should().Be(ScheduledPipelineTaskStatus.Fired);
        row.FiredAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public async Task FireDueAsync_does_not_fire_a_task_twice()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Revert");
        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(Completed());
        ScheduledPipelineService sut = Build(db, clock, engine);
        await sut.ScheduleAsync(ChannelA, PipelineId, 10, Vars(), "1", "A");
        clock.Advance(TimeSpan.FromSeconds(11));

        (await sut.FireDueAsync()).Should().Be(1);
        (await sut.FireDueAsync()).Should().Be(0, "a fired task is terminal and must not re-fire");

        await engine
            .Received(1)
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FireDueAsync_leaves_a_not_yet_due_task_pending()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Revert");
        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        ScheduledPipelineService sut = Build(db, clock, engine);
        await sut.ScheduleAsync(ChannelA, PipelineId, 300, Vars(), "1", "A");

        clock.Advance(TimeSpan.FromSeconds(30)); // still 270s early
        int fired = await sut.FireDueAsync();

        fired.Should().Be(0);
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Pending);
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task FireDueAsync_expires_a_task_overdue_beyond_the_stale_grace_without_dispatching()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Revert");
        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        ScheduledPipelineService sut = Build(db, clock, engine);
        await sut.ScheduleAsync(ChannelA, PipelineId, 10, Vars(), "1", "A");

        // Simulate a long downtime: the task came due 10s in, but we only sweep well past the grace window.
        clock.Advance(ScheduledPipelineService.StaleGrace + TimeSpan.FromMinutes(5));
        int handled = await sut.FireDueAsync();

        handled.Should().Be(1);
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Expired);
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default); // a long-late revert must NOT run
    }

    [Fact]
    public async Task FireDueAsync_marks_fired_even_when_the_target_pipeline_dispatch_fails()
    {
        AuthDbContext db = await SeedPipelineAsync(ChannelA, PipelineId, "Revert");
        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        // The engine's fail-closed shape for a deleted / unparseable target: a Failed result, never a throw.
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "x",
                    Outcome = PipelineOutcome.Failed,
                    Duration = TimeSpan.Zero,
                    ErrorMessage = "Pipeline was deleted.",
                }
            );
        ScheduledPipelineService sut = Build(db, clock, engine);
        await sut.ScheduleAsync(ChannelA, PipelineId, 10, Vars(), "1", "A");
        clock.Advance(TimeSpan.FromSeconds(11));

        int fired = await sut.FireDueAsync();

        // The sweep survives a failed dispatch and the row is still terminal (no poisoned pending row lingers).
        fired.Should().Be(1);
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Fired);
    }

    private static Task<PipelineExecutionResult> Completed() =>
        Task.FromResult(
            new PipelineExecutionResult
            {
                ExecutionId = "x",
                Outcome = PipelineOutcome.Completed,
                Duration = TimeSpan.Zero,
            }
        );
}
