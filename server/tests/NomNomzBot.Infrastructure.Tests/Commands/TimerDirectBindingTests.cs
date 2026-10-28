// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A timer binds a command or an action directly: the management service validates and stores the target
/// through the shared binder, the target round-trips on the DTO, the usage source reports the timer as a
/// surface that runs the command, and the command guard refuses a delete while such a timer exists.
/// </summary>
public sealed class TimerDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f501");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f502");

    private static (TimerManagementService Sut, CommandsTestDbContext Db) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        TimerManagementService sut = new(
            db,
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator()
        );
        return (sut, db);
    }

    private static Command SeedCommand(CommandsTestDbContext db, Guid channel, string name)
    {
        Command command = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = channel,
            Name = name,
            NameNormalized = name,
            Tier = "template",
            TemplateResponse = "drink water",
        };
        db.Commands.Add(command);
        db.SaveChanges();
        return command;
    }

    [Fact]
    public async Task Create_with_a_command_target_stores_it_and_returns_it_on_the_dto()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        Command command = SeedCommand(db, Channel, "hydrate");

        Result<TimerDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new CreateTimerDto
            {
                Name = "hydrate-timer",
                Messages = [],
                Target = new(BindingTargetKind.Command, command.Id.ToString()),
            }
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Command, command.Id.ToString()));
        DomainTimer stored = db.Timers.Single();
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(command.Id);
        stored.PipelineId.Should().BeNull();
    }

    [Fact]
    public async Task Create_with_a_command_of_another_channel_is_refused_and_nothing_is_stored()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        Command foreign = SeedCommand(db, OtherChannel, "theirs");

        Result<TimerDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new CreateTimerDto
            {
                Name = "steal",
                Messages = [],
                Target = new(BindingTargetKind.Command, foreign.Id.ToString()),
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.Timers.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_swaps_a_command_target_for_an_action_target()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        Command command = SeedCommand(db, Channel, "hydrate");
        TimerDto created = (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateTimerDto
                {
                    Name = "t",
                    Messages = [],
                    Target = new(BindingTargetKind.Command, command.Id.ToString()),
                }
            )
        ).Value;

        Result<TimerDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            created.Id,
            new UpdateTimerDto { Target = new(BindingTargetKind.Action, ActionType: "clear_chat") }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Target!.Kind.Should().Be(BindingTargetKind.Action);
        result.Value.Target.ActionType.Should().Be("clear_chat");
        DomainTimer stored = db.Timers.Single();
        stored.TargetKind.Should().Be(BindingTargetKind.Action);
        stored.TargetCommandId.Should().BeNull();
        stored.TargetActionType.Should().Be("clear_chat");
    }

    [Fact]
    public async Task Update_with_a_pipeline_target_without_an_id_clears_the_binding()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        Command command = SeedCommand(db, Channel, "hydrate");
        TimerDto created = (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateTimerDto
                {
                    Name = "t",
                    Messages = ["hi"],
                    Target = new(BindingTargetKind.Command, command.Id.ToString()),
                }
            )
        ).Value;

        Result<TimerDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            created.Id,
            new UpdateTimerDto { Target = new(BindingTargetKind.Pipeline) }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Target.Should().BeNull();
        db.Timers.Single().TargetCommandId.Should().BeNull();
    }

    [Fact]
    public async Task Usage_source_reports_only_the_timer_bound_to_the_command()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        Command command = SeedCommand(db, Channel, "hydrate");
        TimerDto bound = (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateTimerDto
                {
                    Name = "bound",
                    Messages = [],
                    Target = new(BindingTargetKind.Command, command.Id.ToString()),
                }
            )
        ).Value;
        await sut.CreateAsync(
            Channel.ToString(),
            new CreateTimerDto { Name = "plain", Messages = ["hi"] }
        );

        IReadOnlyList<BindingUsage> usages = await new TimerBindingUsageSource(db).FindUsagesAsync(
            Channel,
            command.Id.ToString()
        );

        usages.Should().Equal(new BindingUsage("timers", bound.Id.ToString(), "bound"));
    }

    [Fact]
    public async Task Deleting_a_command_a_timer_runs_is_refused_and_lists_the_timers_surface()
    {
        (TimerManagementService sut, CommandsTestDbContext db) = Build();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "timer-guard-channel",
                NameNormalized = "timer-guard-channel",
            }
        );
        db.SaveChanges();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new TimerBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;
        TimerDto timer = (
            await sut.CreateAsync(
                Channel.ToString(),
                new CreateTimerDto
                {
                    Name = "hydrate-timer",
                    Messages = [],
                    Target = new(BindingTargetKind.Command, created.Id.ToString()),
                }
            )
        ).Value;

        Result result = await commands.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .Equal(new BindingUsage("timers", timer.Id.ToString(), "hydrate-timer"));
        db.Commands.Should().ContainSingle(c => c.Id == created.Id);
    }
}
