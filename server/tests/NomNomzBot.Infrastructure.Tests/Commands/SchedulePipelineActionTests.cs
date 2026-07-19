// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the <c>schedule_pipeline</c> pipeline action end to end over the REAL
/// <see cref="ScheduledPipelineService"/>: a valid call resolves the named pipeline to its id, captures the
/// current context variables, and persists one pending deferred task with the template-resolved dedupe key; an
/// unknown pipeline name is a typed failure that schedules nothing; a missing name / delay fails without touching
/// the store.
/// </summary>
public sealed class SchedulePipelineActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000a001");
    private static readonly Guid PipelineId = Guid.Parse("0192d000-0000-7000-8000-0000000000f1");
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AuthDbContext> SeedAsync(string name)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Pipelines.Add(
            new PipelineEntity
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = name,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static PipelineExecutionContext Context(params (string, string)[] vars)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "555",
            TriggeredByDisplayName = "Bamo",
            MessageId = string.Empty,
            RawMessage = string.Empty,
        };
        foreach ((string k, string v) in vars)
            ctx.Variables[k] = v;
        return ctx;
    }

    private static ActionDefinition Action(params (string, object)[] parameters)
    {
        Dictionary<string, JsonElement> map = parameters.ToDictionary(
            p => p.Item1,
            p => JsonSerializer.SerializeToElement(p.Item2)
        );
        return new ActionDefinition { Type = "schedule_pipeline", Parameters = map };
    }

    private static (SchedulePipelineAction Action, ScheduledPipelineService Service) Build(
        AuthDbContext db,
        ITemplateResolver resolver
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(Substitute.For<IPipelineEngine>());
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        ScheduledPipelineService service = new(
            db,
            scopeFactory,
            new FakeTimeProvider(Start),
            NullLogger<ScheduledPipelineService>.Instance
        );
        return (new SchedulePipelineAction(service, resolver), service);
    }

    [Fact]
    public async Task Valid_call_schedules_one_task_with_the_resolved_id_captured_vars_and_dedupe_key()
    {
        AuthDbContext db = await SeedAsync("Voice Swap Revert");
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                "voice-swap-revert:{user.id}",
                Arg.Any<IDictionary<string, string>>(),
                Channel,
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult("voice-swap-revert:555"));
        (SchedulePipelineAction action, _) = Build(db, resolver);

        ActionResult result = await action.ExecuteAsync(
            Context(("user.id", "555"), ("revert.to", "en-US-Aria")),
            Action(
                ("pipeline", "voice swap revert"),
                ("delay_seconds", 300),
                ("dedupe_key", "voice-swap-revert:{user.id}")
            )
        );

        result.Succeeded.Should().BeTrue();
        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.PipelineId.Should().Be(PipelineId);
        row.PipelineName.Should().Be("Voice Swap Revert");
        row.DueAt.Should().Be(Start.AddSeconds(300));
        row.DedupeKey.Should().Be("voice-swap-revert:555");
        row.TriggeredByUserId.Should().Be("555");
        // The current context variables were captured for the deferred run.
        row.VariablesJson.Should().Contain("revert.to").And.Contain("en-US-Aria");
        row.VariablesJson.Should().Contain("user.id");
    }

    [Fact]
    public async Task An_unknown_pipeline_name_fails_and_schedules_nothing()
    {
        AuthDbContext db = await SeedAsync("Real Pipeline");
        (SchedulePipelineAction action, _) = Build(db, Substitute.For<ITemplateResolver>());

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("pipeline", "Ghost Pipeline"), ("delay_seconds", 60))
        );

        result.Succeeded.Should().BeFalse();
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_missing_pipeline_parameter_fails_without_scheduling()
    {
        AuthDbContext db = await SeedAsync("P");
        (SchedulePipelineAction action, _) = Build(db, Substitute.For<ITemplateResolver>());

        ActionResult result = await action.ExecuteAsync(Context(), Action(("delay_seconds", 60)));

        result.Succeeded.Should().BeFalse();
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_missing_or_zero_delay_fails_without_scheduling()
    {
        AuthDbContext db = await SeedAsync("P");
        (SchedulePipelineAction action, _) = Build(db, Substitute.For<ITemplateResolver>());

        ActionResult result = await action.ExecuteAsync(
            Context(),
            Action(("pipeline", "P"), ("delay_seconds", 0))
        );

        result.Succeeded.Should().BeFalse();
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }
}
