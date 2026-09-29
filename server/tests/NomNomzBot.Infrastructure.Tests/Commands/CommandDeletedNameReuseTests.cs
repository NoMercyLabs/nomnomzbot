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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Tests.Seeding;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A deleted command is a soft-deleted tombstone. On the PRODUCTION context (<see cref="RealCommandsDb"/>)
/// these prove a deleted name can be used again, and that no automatic seeder brings back a command the
/// channel deleted.
/// </summary>
public sealed class CommandDeletedNameReuseTests : IDisposable
{
    private static readonly Guid Channel = RealCommandsDb.Channel;

    private readonly RealCommandsDb _real = new();
    private CommandService Commands => _real.Commands;

    public void Dispose() => _real.Dispose();

    [Fact]
    public async Task A_deleted_command_name_can_be_created_again_as_a_fresh_command()
    {
        CommandDto original = (
            await Commands.CreateAsync(
                Channel.ToString(),
                new() { Name = "8ball", TemplateResponse = "old answer" }
            )
        ).Value;
        (await Commands.DeleteAsync(Channel.ToString(), "8ball")).IsSuccess.Should().BeTrue();

        Result<CommandDto> recreated = await Commands.CreateAsync(
            Channel.ToString(),
            new() { Name = "8ball", TemplateResponse = "new answer" }
        );

        recreated.IsSuccess.Should().BeTrue(recreated.ErrorMessage);
        recreated.Value.Id.Should().NotBe(original.Id);
        (await Commands.GetAsync(Channel.ToString(), "8ball"))
            .Value.TemplateResponse.Should()
            .Be("new answer");

        // The deleted row stays as history; exactly one live row answers to the name.
        List<Command> rows = await _real
            .Db.Commands.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == Channel && c.NameNormalized == "8ball")
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(c => c.DeletedAt == null && c.Id == recreated.Value.Id);
        rows.Should().ContainSingle(c => c.DeletedAt != null && c.Id == original.Id);
    }

    [Fact]
    public async Task A_command_renamed_onto_a_deleted_name_saves()
    {
        await Commands.CreateAsync(
            Channel.ToString(),
            new() { Name = "hug", TemplateResponse = "hug" }
        );
        await Commands.DeleteAsync(Channel.ToString(), "hug");
        await Commands.CreateAsync(
            Channel.ToString(),
            new() { Name = "cuddle", TemplateResponse = "cuddle" }
        );

        Result<CommandDto> renamed = await Commands.UpdateAsync(
            Channel.ToString(),
            "cuddle",
            new() { Name = "hug" }
        );

        renamed.IsSuccess.Should().BeTrue(renamed.ErrorMessage);
        (await Commands.GetAsync(Channel.ToString(), "hug"))
            .Value.TemplateResponse.Should()
            .Be("cuddle");
    }

    [Fact]
    public async Task The_fun_pack_backfill_never_brings_back_a_preset_the_channel_deleted_and_logs_no_error()
    {
        ListLogger<FunCommandPresetPackSeedOnOnboardingHandler> logger = new();
        FunCommandPresetPackSeedOnOnboardingHandler handler = new(_real.Presets, logger);
        await handler.HandleAsync(Onboarded());
        (await Commands.DeleteAsync(Channel.ToString(), "8ball")).IsSuccess.Should().BeTrue();
        logger.Entries.Clear();

        // The startup backfill re-publishes onboarding on every boot.
        await handler.HandleAsync(Onboarded());

        (await Commands.GetAsync(Channel.ToString(), "8ball")).ErrorCode.Should().Be("NOT_FOUND");
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        (await _real.Db.Commands.CountAsync(c => c.BroadcasterId == Channel)).Should().Be(5);
    }

    [Fact]
    public async Task The_raid_flow_seeder_never_brings_back_a_raid_command_the_channel_deleted()
    {
        RaidFlowSeeder seeder = new(_real.Db);
        await seeder.SeedAsync(Channel);
        (await Commands.DeleteAsync(Channel.ToString(), "raid")).IsSuccess.Should().BeTrue();

        Func<Task> reseed = () => seeder.SeedAsync(Channel);

        await reseed.Should().NotThrowAsync();
        (await Commands.GetAsync(Channel.ToString(), "raid")).ErrorCode.Should().Be("NOT_FOUND");
    }

    private static ChannelOnboardedEvent Onboarded() =>
        new()
        {
            BroadcasterId = Channel,
            OwnerUserId = RealCommandsDb.Owner,
            TwitchChannelId = "tw-channel-c5",
            Name = "reuse",
        };
}
