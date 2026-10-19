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
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using ActionDefinition = NomNomzBot.Application.Abstractions.Pipeline.ActionDefinition;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The <c>schedule_pipeline</c> action is the caller of the direct scheduled-task target: it can name a command
/// (by name, resolved inside this channel) or an action type instead of a pipeline, and the task is stored as a
/// command or action binding with no wrapper pipeline.
/// </summary>
public sealed class SchedulePipelineActionDirectTargetTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000b001");
    private static readonly Guid OtherChannel = Guid.Parse("0192d000-0000-7000-8000-00000000b002");
    private static readonly Guid CommandId = Guid.Parse("0192d000-0000-7000-8000-00000000b003");
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static async Task<AuthDbContext> SeedCommandAsync(Guid channel, string name)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Commands.Add(
            new()
            {
                Id = CommandId,
                BroadcasterId = channel,
                Name = name,
                NameNormalized = name.ToLowerInvariant(),
                Tier = "template",
                TemplateResponse = "drink water",
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "555",
            TriggeredByDisplayName = "Bamo",
            MessageId = string.Empty,
            RawMessage = string.Empty,
        };

    private static ActionDefinition Action(params (string, object)[] parameters) =>
        new()
        {
            Type = "schedule_pipeline",
            Parameters = parameters.ToDictionary(
                p => p.Item1,
                p => JsonSerializer.SerializeToElement(p.Item2)
            ),
        };

    private static SchedulePipelineAction Build(AuthDbContext db)
    {
        IServiceScopeFactory scopeFactory = new ServiceCollection()
            .AddSingleton(Substitute.For<IPipelineEngine>())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        ScheduledPipelineService service = new(
            db,
            new ScheduledPipelineDispatcher(
                scopeFactory,
                NullLogger<ScheduledPipelineDispatcher>.Instance
            ),
            new FakeTimeProvider(Start),
            NullLogger<ScheduledPipelineService>.Instance
        );
        return new(service, Substitute.For<ITemplateResolver>());
    }

    [Fact]
    public async Task A_command_name_schedules_a_direct_command_task_with_no_pipeline()
    {
        AuthDbContext db = await SeedCommandAsync(Channel, "hydrate");

        ActionResult result = await Build(db)
            .ExecuteAsync(Context(), Action(("command", "!Hydrate"), ("delay_seconds", 90)));

        result.Succeeded.Should().BeTrue();
        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.TargetKind.Should().Be(BindingTargetKind.Command);
        row.TargetCommandId.Should().Be(CommandId);
        row.PipelineId.Should().Be(Guid.Empty);
        row.DueAt.Should().Be(Start.AddSeconds(90));
        row.TriggeredByUserId.Should().Be("555");
    }

    [Fact]
    public async Task A_command_of_another_channel_is_refused_and_nothing_is_stored()
    {
        AuthDbContext db = await SeedCommandAsync(OtherChannel, "elsewhere");

        ActionResult result = await Build(db)
            .ExecuteAsync(Context(), Action(("command", "elsewhere"), ("delay_seconds", 90)));

        result.Succeeded.Should().BeFalse();
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_action_type_with_params_schedules_a_direct_action_task()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        ActionResult result = await Build(db)
            .ExecuteAsync(
                Context(),
                Action(
                    ("action_type", "send_message"),
                    ("action_params", new Dictionary<string, string> { ["message"] = "hi" }),
                    ("delay_seconds", 60)
                )
            );

        result.Succeeded.Should().BeTrue();
        ScheduledPipelineTask row = await db.ScheduledPipelineTasks.SingleAsync();
        row.TargetKind.Should().Be(BindingTargetKind.Action);
        row.TargetActionType.Should().Be("send_message");
        row.TargetActionParamsJson.Should().Contain("message").And.Contain("hi");
        row.PipelineId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Naming_two_targets_at_once_fails_without_scheduling()
    {
        AuthDbContext db = await SeedCommandAsync(Channel, "hydrate");

        ActionResult result = await Build(db)
            .ExecuteAsync(
                Context(),
                Action(
                    ("command", "hydrate"),
                    ("action_type", "send_message"),
                    ("delay_seconds", 60)
                )
            );

        result.Succeeded.Should().BeFalse();
        (await db.ScheduledPipelineTasks.CountAsync()).Should().Be(0);
    }
}
