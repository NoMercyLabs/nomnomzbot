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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S068f (legacy builtins audit — seeded fun-command preset pack): proves a fresh
/// <see cref="ChannelOnboardedEvent"/> leaves the real preset commands persisted and readable through
/// <see cref="CommandService"/>'s own read path (<c>ListAsync</c>) — not merely "the handler didn't
/// throw" — and that re-firing onboarding for an already-seeded channel does not duplicate them, mirroring
/// <c>SystemWidgetSeedOnOnboardingHandlerTests</c>' idempotency coverage for the widgets domain.
/// </summary>
public sealed class FunCommandPresetPackSeedOnOnboardingHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000e0f68");

    private static CommandsTestDbContext NewDb()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "fun-preset-channel",
                NameNormalized = "fun-preset-channel",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static (
        FunCommandPresetPackSeedOnOnboardingHandler Handler,
        CommandsTestDbContext Db,
        CommandService CommandService
    ) Build()
    {
        CommandsTestDbContext db = NewDb();
        CommandService commandService = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            TestQuota.Unlimited(),
            new TemplateHelperValidator()
        );
        FunCommandPresetPackSeedOnOnboardingHandler handler = new(
            commandService,
            NullLogger<FunCommandPresetPackSeedOnOnboardingHandler>.Instance
        );
        return (handler, db, commandService);
    }

    private static ChannelOnboardedEvent OnboardedEvent() =>
        new()
        {
            BroadcasterId = Channel,
            OwnerUserId = Channel,
            TwitchChannelId = "555",
            Name = "fun-preset-channel",
        };

    [Fact]
    public async Task Handle_persists_the_preset_pack_as_real_commands_readable_via_CommandService()
    {
        (
            FunCommandPresetPackSeedOnOnboardingHandler handler,
            CommandsTestDbContext db,
            CommandService commandService
        ) = Build();

        await handler.HandleAsync(OnboardedEvent());

        PagedList<CommandListItem> listed = (
            await commandService.ListAsync(Channel.ToString(), new(1, 50))
        ).Value;

        listed
            .Items.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["8ball", "hug", "slap", "ping", "rps", "compliment"]);

        // Persisted as real Command rows in the database the read path just queried — not a fabricated DTO.
        db.Commands.Count(c => c.BroadcasterId == Channel).Should().Be(6);
    }

    [Fact]
    public async Task Handle_ignores_the_platform_level_sentinel_broadcaster_id()
    {
        (FunCommandPresetPackSeedOnOnboardingHandler handler, CommandsTestDbContext db, _) =
            Build();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "555",
                Name = "fun-preset-channel",
            }
        );

        db.Commands.Any().Should().BeFalse();
    }

    [Fact]
    public async Task Handle_re_fired_for_an_already_seeded_channel_does_not_duplicate_the_preset_pack()
    {
        (
            FunCommandPresetPackSeedOnOnboardingHandler handler,
            CommandsTestDbContext db,
            CommandService commandService
        ) = Build();
        await handler.HandleAsync(OnboardedEvent());

        await handler.HandleAsync(OnboardedEvent());

        db.Commands.Count(c => c.BroadcasterId == Channel).Should().Be(6);
        PagedList<CommandListItem> listed = (
            await commandService.ListAsync(Channel.ToString(), new(1, 50))
        ).Value;
        listed
            .Items.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["8ball", "hug", "slap", "ping", "rps", "compliment"]);
    }

    [Fact]
    public async Task Handle_does_not_clobber_a_streamer_authored_command_that_reuses_a_preset_name()
    {
        (
            FunCommandPresetPackSeedOnOnboardingHandler handler,
            CommandsTestDbContext db,
            CommandService commandService
        ) = Build();
        Result<CommandDto> ownCommand = await commandService.CreateAsync(
            Channel.ToString(),
            new() { Name = "ping", TemplateResponse = "streamer's own !ping" }
        );
        ownCommand.IsSuccess.Should().BeTrue();

        await handler.HandleAsync(OnboardedEvent());

        Result<CommandDto> stillOwned = await commandService.GetAsync(Channel.ToString(), "ping");
        stillOwned.Value.TemplateResponse.Should().Be("streamer's own !ping");
        db.Commands.Count(c => c.BroadcasterId == Channel).Should().Be(6);
    }
}
