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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Jobs;

/// <summary>
/// Proves the background sweeper drives the scheduler on each tick: a due task, once its moment has passed, is
/// dispatched through the pipeline engine and left terminal — the durable side of the deferred-execution primitive
/// (a delayed action still fires across a restart, since the very tick that runs after boot IS the startup sweep).
/// </summary>
public sealed class ScheduledPipelineExpiryServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-00000000a001");
    private static readonly Guid PipelineId = Guid.Parse("0192c000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_single_tick_fires_a_task_that_came_due_and_leaves_it_terminal()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "Revert",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new PipelineExecutionResult
                    {
                        ExecutionId = "x",
                        Outcome = PipelineOutcome.Completed,
                        Duration = TimeSpan.Zero,
                    }
                )
            );

        // A minimal DI graph so the sweeper's per-tick scope resolves the real scheduler over these fakes.
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(engine);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<IScheduledPipelineService, ScheduledPipelineService>();
        services.AddSingleton<ILogger<ScheduledPipelineService>>(
            NullLogger<ScheduledPipelineService>.Instance
        );
        ServiceProvider provider = services.BuildServiceProvider();

        // Seed a task due 10s out, then let the clock pass its due time before the tick runs.
        IScheduledPipelineService scheduler =
            provider.GetRequiredService<IScheduledPipelineService>();
        await scheduler.ScheduleAsync(
            Channel,
            PipelineId,
            10,
            new Dictionary<string, string> { ["revert.to"] = "en-US-Aria" },
            "555",
            "Bamo"
        );
        clock.Advance(TimeSpan.FromSeconds(11));

        ScheduledPipelineExpiryService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ScheduledPipelineExpiryService>.Instance
        );
        await sut.TickAsync(CancellationToken.None);

        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Channel && r.PipelineId == PipelineId
                ),
                Arg.Any<CancellationToken>()
            );
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Fired);
    }

    [Fact]
    public async Task A_tick_with_nothing_due_dispatches_nothing()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "Revert",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();

        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(engine);
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<IScheduledPipelineService, ScheduledPipelineService>();
        services.AddSingleton<ILogger<ScheduledPipelineService>>(
            NullLogger<ScheduledPipelineService>.Instance
        );
        ServiceProvider provider = services.BuildServiceProvider();

        await provider
            .GetRequiredService<IScheduledPipelineService>()
            .ScheduleAsync(Channel, PipelineId, 3600, new Dictionary<string, string>(), "1", "A");

        ScheduledPipelineExpiryService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ScheduledPipelineExpiryService>.Instance
        );
        await sut.TickAsync(CancellationToken.None);

        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        (await db.ScheduledPipelineTasks.SingleAsync())
            .Status.Should()
            .Be(ScheduledPipelineTaskStatus.Pending);
    }

    /// <summary>
    /// S037 — a throwing tick used to skip its inter-tick delay (the delay sat INSIDE the try, so an
    /// exception jumped straight past it), spinning the loop hot against whatever just failed. The delay
    /// now runs whether the tick threw or not: a second attempt must NOT happen until a full
    /// <c>TickInterval</c> (5s) has elapsed on the (fake, controllable) clock.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenTickThrows_StillWaitsTheFullIntervalBeforeRetrying()
    {
        FakeTimeProvider clock = new(Start);
        ThrowingScopeFactory scopeFactory = new();

        ScheduledPipelineExpiryService sut = new(
            scopeFactory,
            clock,
            NullLogger<ScheduledPipelineExpiryService>.Instance
        );

        using CancellationTokenSource cts = new();
        await sut.StartAsync(cts.Token);
        try
        {
            await WaitUntilAsync(() => scopeFactory.CallCount >= 1);
            scopeFactory.CallCount.Should().Be(1);

            clock.Advance(TimeSpan.FromSeconds(4));
            await Task.Delay(50, CancellationToken.None);
            scopeFactory.CallCount.Should().Be(1, "the failing tick's delay has not elapsed yet");

            clock.Advance(TimeSpan.FromSeconds(1));
            await WaitUntilAsync(() => scopeFactory.CallCount >= 2);
            scopeFactory.CallCount.Should().Be(2);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    /// <summary>A scope factory that always fails to create a scope — simulates a throwing tick.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CallCount { get; private set; }

        public IServiceScope CreateScope()
        {
            CallCount++;
            throw new InvalidOperationException("scope boom");
        }
    }
}
