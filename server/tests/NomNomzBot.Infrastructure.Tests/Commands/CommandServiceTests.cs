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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves <see cref="CommandService"/> publishes the E5 dashboard live-sync event after every successful
/// create/update/delete so a second open dashboard's Commands page refetches, and that a rejected mutation
/// (duplicate name, unknown command) never publishes.
/// </summary>
public sealed class CommandServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");

    private static (CommandService Sut, RecordingEventBus Bus) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        IPipelineEngine pipelineEngine = Substitute.For<IPipelineEngine>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        RecordingEventBus bus = new();
        return (
            new CommandService(db, pipelineEngine, registry, bus, Billing.TestTiers.Unlimited()),
            bus
        );
    }

    private static CreateCommandDto Req(string name = "hello") => new() { Name = name };

    [Fact]
    public async Task Create_publishes_ChannelConfigChangedEvent_for_the_commands_domain()
    {
        (CommandService sut, RecordingEventBus bus) = Build();

        CommandDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "commands"
                && e.EntityId == created.Id.ToString()
                && e.Action == "created"
            );
    }

    [Fact]
    public async Task Create_of_a_duplicate_name_publishes_nothing()
    {
        (CommandService sut, RecordingEventBus bus) = Build();
        await sut.CreateAsync(Channel.ToString(), Req());
        bus.Published.Clear();

        Result<CommandDto> result = await sut.CreateAsync(Channel.ToString(), Req());

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_publishes_an_updated_action()
    {
        (CommandService sut, RecordingEventBus bus) = Build();
        CommandDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        bus.Published.Clear();

        await sut.UpdateAsync(
            Channel.ToString(),
            created.Name,
            new UpdateCommandDto { CooldownSeconds = 30 }
        );

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "commands"
                && e.EntityId == created.Id.ToString()
                && e.Action == "updated"
            );
    }

    [Fact]
    public async Task Update_of_an_unknown_command_publishes_nothing()
    {
        (CommandService sut, RecordingEventBus bus) = Build();

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "missing",
            new UpdateCommandDto()
        );

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_publishes_a_deleted_action()
    {
        (CommandService sut, RecordingEventBus bus) = Build();
        CommandDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        bus.Published.Clear();

        (await sut.DeleteAsync(Channel.ToString(), created.Name)).IsSuccess.Should().BeTrue();

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "commands"
                && e.EntityId == created.Id.ToString()
                && e.Action == "deleted"
            );
    }

    [Fact]
    public async Task Delete_of_an_unknown_command_publishes_nothing()
    {
        (CommandService sut, RecordingEventBus bus) = Build();

        Result result = await sut.DeleteAsync(Channel.ToString(), "missing");

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }
}
