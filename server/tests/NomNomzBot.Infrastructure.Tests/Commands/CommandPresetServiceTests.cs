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
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// The fun-command preset pack: seeding marks each row with its preset, and a reset writes the preset back
/// over a seeded command — keeping the name and on/off state — while a channel-written command is refused.
/// </summary>
public sealed class CommandPresetServiceTests : IDisposable
{
    private static readonly string Channel = RealCommandsDb.Channel.ToString();

    private readonly RealCommandsDb _real = new();

    public void Dispose() => _real.Dispose();

    [Fact]
    public async Task Seeding_marks_every_preset_row_and_the_list_shows_the_mark()
    {
        CommandPresetSeedReport report = (
            await _real.Presets.SeedAsync(RealCommandsDb.Channel)
        ).Value;

        report.Seeded.Should().Be(6);
        PagedList<CommandListItem> listed = (
            await _real.Commands.ListAsync(Channel, new(1, 50))
        ).Value;
        listed.Items.Should().HaveCount(6).And.OnlyContain(c => c.PresetKey == c.Name);
    }

    [Fact]
    public async Task Reset_writes_the_preset_back_and_keeps_the_name_and_on_off_state()
    {
        await _real.Presets.SeedAsync(RealCommandsDb.Channel);
        await _real.Commands.UpdateAsync(
            Channel,
            "8ball",
            new()
            {
                Name = "ball",
                TemplateResponse = "always yes",
                TemplateResponses = ["yes"],
                Description = "mine now",
                MinPermissionLevel = "Moderator",
                CooldownSeconds = 90,
                Aliases = ["orb"],
                IsEnabled = false,
            }
        );
        _real.Bus.Published.Clear();

        Result<CommandDto> reset = await _real.Presets.ResetAsync(Channel, "ball");

        reset.IsSuccess.Should().BeTrue(reset.ErrorMessage);
        Command row = await _real.Db.Commands.SingleAsync(c => c.NameNormalized == "ball");
        row.Name.Should().Be("ball");
        row.IsEnabled.Should().BeFalse();
        row.TemplateResponse.Should().BeNull();
        row.TemplateResponses.Should().HaveCount(8).And.Contain("🎱 It is certain.");
        row.Description.Should().Be("Ask the magic 8-ball a yes/no question.");
        row.MinPermissionLevel.Should().Be(0);
        row.CooldownSeconds.Should().Be(0);
        row.Aliases.Should().BeEmpty();
        row.PresetKey.Should().Be("8ball");
        reset.Value.TemplateResponses.Should().BeEquivalentTo(row.TemplateResponses);

        _real
            .Bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.Domain == "commands" && e.EntityId == row.Id.ToString() && e.Action == "updated"
            );
        await _real
            .Registry.Received()
            .InvalidateCommandsAsync(RealCommandsDb.Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_refuses_a_command_the_channel_wrote_and_leaves_it_untouched()
    {
        await _real.Commands.CreateAsync(
            Channel,
            new() { Name = "ping", TemplateResponse = "my own pong" }
        );

        Result<CommandDto> reset = await _real.Presets.ResetAsync(Channel, "ping");

        reset.ErrorCode.Should().Be("NOT_A_PRESET");
        (await _real.Commands.GetAsync(Channel, "ping"))
            .Value.TemplateResponse.Should()
            .Be("my own pong");
    }

    [Fact]
    public async Task Seeding_adopts_an_unmarked_preset_row_but_never_a_same_named_command_the_channel_wrote()
    {
        // Rows seeded before the mark existed: one still carries the preset's description, one does not.
        await _real.Commands.CreateAsync(
            Channel,
            new()
            {
                Name = "hug",
                TemplateResponse = "reworded hug",
                Description = "Give someone a hug.",
            }
        );
        await _real.Commands.CreateAsync(
            Channel,
            new() { Name = "slap", TemplateResponse = "my own slap" }
        );

        CommandPresetSeedReport report = (
            await _real.Presets.SeedAsync(RealCommandsDb.Channel)
        ).Value;

        report.AlreadyPresent.Should().Be(2);
        report.Seeded.Should().Be(4);
        (await _real.Commands.GetAsync(Channel, "hug")).Value.PresetKey.Should().Be("hug");
        (await _real.Commands.GetAsync(Channel, "slap")).Value.PresetKey.Should().BeNull();
        (await _real.Commands.GetAsync(Channel, "slap"))
            .Value.TemplateResponse.Should()
            .Be("my own slap");
    }

    [Fact]
    public void The_preset_list_carries_every_field_a_reset_restores()
    {
        IReadOnlyList<CommandPresetDto> presets = _real.Presets.ListPresets();

        presets
            .Select(p => p.Key)
            .Should()
            .BeEquivalentTo(["8ball", "hug", "slap", "ping", "rps", "compliment"]);
        CommandPresetDto eightBall = presets.Single(p => p.Key == "8ball");
        eightBall.TemplateResponses.Should().HaveCount(8);
        eightBall.TemplateResponse.Should().BeNull();
        eightBall.MinPermissionLevel.Should().Be("Everyone");
        eightBall.Tier.Should().Be("template");
    }
}
