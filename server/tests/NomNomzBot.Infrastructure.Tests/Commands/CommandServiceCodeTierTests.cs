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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A command of tier "code" holds its script itself. It needs one, the script must belong to the same channel,
/// and a refused request stores nothing. Moving the command to another tier lets go of the script.
/// </summary>
public sealed class CommandServiceCodeTierTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000ec101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000ec102");

    private static (
        CommandService Sut,
        CommandsTestDbContext Db,
        Guid OwnScript,
        Guid ForeignScript
    ) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        Guid own = Guid.CreateVersion7();
        Guid foreign = Guid.CreateVersion7();
        db.CodeScripts.Add(
            new CodeScript
            {
                Id = own,
                BroadcasterId = Channel,
                Name = "own",
            }
        );
        db.CodeScripts.Add(
            new CodeScript
            {
                Id = foreign,
                BroadcasterId = OtherChannel,
                Name = "foreign",
            }
        );
        db.SaveChanges();
        CommandService sut = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([])
        );
        return (sut, db, own, foreign);
    }

    [Fact]
    public async Task A_code_command_with_a_script_from_another_channel_is_refused_and_stores_nothing()
    {
        (CommandService sut, CommandsTestDbContext db, _, Guid foreign) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "lucky",
                Tier = "code",
                CodeScriptId = foreign,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CODE_SCRIPT_NOT_IN_CHANNEL");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_code_command_without_a_script_is_refused_with_a_clear_message_and_stores_nothing()
    {
        (CommandService sut, CommandsTestDbContext db, _, _) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "lucky", Tier = "code" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("code script");
        (await db.Commands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_code_command_with_its_own_channels_script_stores_the_script_and_no_pipeline()
    {
        (CommandService sut, CommandsTestDbContext db, Guid own, _) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "lucky",
                Tier = "code",
                CodeScriptId = own,
            }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeScriptId.Should().Be(own);
        result.Value.PipelineId.Should().BeNull();
        NomNomzBot.Domain.Commands.Entities.Command stored = await db.Commands.SingleAsync();
        stored.Tier.Should().Be("code");
        stored.CodeScriptId.Should().Be(own);
        stored.PipelineId.Should().BeNull();
    }

    [Fact]
    public async Task Updating_a_code_command_to_a_script_from_another_channel_is_refused_and_keeps_the_old_script()
    {
        (CommandService sut, CommandsTestDbContext db, Guid own, Guid foreign) = Build();
        await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "lucky",
                Tier = "code",
                CodeScriptId = own,
            }
        );

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "lucky",
            new() { CodeScriptId = foreign }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CODE_SCRIPT_NOT_IN_CHANNEL");
        db.ChangeTracker.Clear();
        (await db.Commands.SingleAsync()).CodeScriptId.Should().Be(own);
    }

    [Fact]
    public async Task Moving_a_code_command_to_the_template_tier_lets_go_of_its_script()
    {
        (CommandService sut, CommandsTestDbContext db, Guid own, _) = Build();
        await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "lucky",
                Tier = "code",
                CodeScriptId = own,
            }
        );

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "lucky",
            new() { Tier = "template", TemplateResponse = "hello" }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeScriptId.Should().BeNull();
        db.ChangeTracker.Clear();
        (await db.Commands.SingleAsync()).CodeScriptId.Should().BeNull();
    }
}
