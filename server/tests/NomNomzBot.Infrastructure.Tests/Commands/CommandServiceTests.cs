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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
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

    /// <summary>A fresh relational test database with the owning channel row seeded — every audit
    /// <c>Record</c> the service writes carries a real <c>BroadcasterId</c> foreign key, which the
    /// relational test database enforces.</summary>
    private static CommandsTestDbContext NewDb()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "command-service-channel",
                NameNormalized = "command-service-channel",
            }
        );
        db.SaveChanges();
        return db;
    }

    private static (CommandService Sut, RecordingEventBus Bus) Build()
    {
        CommandsTestDbContext db = NewDb();
        IPipelineEngine pipelineEngine = Substitute.For<IPipelineEngine>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        RecordingEventBus bus = new();
        return (
            new(
                db,
                pipelineEngine,
                registry,
                bus,
                Billing.TestQuota.Unlimited(),
                new TemplateHelperValidator()
            ),
            bus
        );
    }

    private static CreateCommandDto Req(string name = "hello") =>
        new() { Name = name, TemplateResponse = "hi there" };

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

        await sut.UpdateAsync(Channel.ToString(), created.Name, new() { CooldownSeconds = 30 });

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

        Result<CommandDto> result = await sut.UpdateAsync(Channel.ToString(), "missing", new());

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

    // ─── S013: deleting a command leaves an audit trail naming the actor ──────

    [Fact]
    public async Task Delete_WithAnActor_RecordsAnAuditRowNamingThemBeforeRemovingTheCommand()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator()
        );
        CommandDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;
        string actorId = Guid.Parse("0192a000-0000-7000-8000-0000000000ea").ToString();

        Result result = await sut.DeleteAsync(Channel.ToString(), created.Name, actorId);

        result.IsSuccess.Should().BeTrue();
        List<NomNomzBot.Domain.Platform.Entities.Record> rows = await db
            .Records.Where(r => r.RecordType == "command_action")
            .ToListAsync();
        rows.Should().ContainSingle();
        NomNomzBot.Domain.Platform.Entities.Record row = rows.Single();
        row.BroadcasterId.Should().Be(Channel);
        row.UserId.Should().Be(actorId);
        row.Data.Should().Contain(created.Name);
    }

    [Fact]
    public async Task Delete_WithNoActorSupplied_StillRecordsAnAuditRow()
    {
        CommandsTestDbContext db = NewDb();
        CommandService sut = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator()
        );
        CommandDto created = (await sut.CreateAsync(Channel.ToString(), Req())).Value;

        await sut.DeleteAsync(Channel.ToString(), created.Name);

        // No actor was supplied — the row still exists (never silently skipped), attributed to the channel
        // itself so an unattributed deletion is at least visible in the trail, never invisible.
        NomNomzBot.Domain.Platform.Entities.Record row = await db.Records.SingleAsync(r =>
            r.RecordType == "command_action"
        );
        row.UserId.Should().Be(Channel.ToString());
    }

    [Fact]
    public async Task Create_persists_prefix_match_and_per_user_cooldown_fields()
    {
        (CommandService sut, _) = Build();

        CreateCommandDto request = new()
        {
            Name = "greet",
            TemplateResponse = "hi there",
            PrefixMode = "Custom",
            CustomPrefix = "?",
            MatchMode = "Regex",
            MatchPattern = "^gr[ae]et$",
            CooldownSeconds = 60,
            CooldownPerUser = true,
            UserCooldownSeconds = 15,
            MinPermissionLevel = 10,
            IsEnabled = false,
        };

        CommandDto created = (await sut.CreateAsync(Channel.ToString(), request)).Value;

        // The returned shape carries the newly-exposed fields verbatim…
        created.PrefixMode.Should().Be("Custom");
        created.CustomPrefix.Should().Be("?");
        created.MatchMode.Should().Be("Regex");
        created.MatchPattern.Should().Be("^gr[ae]et$");
        created.UserCooldownSeconds.Should().Be(15);
        created.CooldownPerUser.Should().BeTrue();
        created.MinPermissionLevel.Should().Be(10);
        created.IsEnabled.Should().BeFalse();

        // …and they are actually persisted (a re-fetch reads them back, not just the create echo).
        CommandDto fetched = (await sut.GetAsync(Channel.ToString(), "greet")).Value;
        fetched.PrefixMode.Should().Be("Custom");
        fetched.CustomPrefix.Should().Be("?");
        fetched.MatchMode.Should().Be("Regex");
        fetched.MatchPattern.Should().Be("^gr[ae]et$");
        fetched.UserCooldownSeconds.Should().Be(15);
        fetched.CooldownPerUser.Should().BeTrue();
        fetched.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Update_patches_prefix_match_and_per_user_cooldown_fields()
    {
        (CommandService sut, _) = Build();
        await sut.CreateAsync(Channel.ToString(), Req("greet"));

        await sut.UpdateAsync(
            Channel.ToString(),
            "greet",
            new()
            {
                PrefixMode = "None",
                MatchMode = "Regex",
                MatchPattern = "^hey$",
                UserCooldownSeconds = 42,
                CooldownPerUser = true,
            }
        );

        CommandDto fetched = (await sut.GetAsync(Channel.ToString(), "greet")).Value;
        fetched.PrefixMode.Should().Be("None");
        fetched.MatchMode.Should().Be("Regex");
        fetched.MatchPattern.Should().Be("^hey$");
        fetched.UserCooldownSeconds.Should().Be(42);
        fetched.CooldownPerUser.Should().BeTrue();
    }

    [Fact]
    public async Task Update_with_empty_custom_prefix_clears_it()
    {
        (CommandService sut, _) = Build();
        await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Name = "greet",
                TemplateResponse = "hi there",
                PrefixMode = "Custom",
                CustomPrefix = "?",
            }
        );

        await sut.UpdateAsync(
            Channel.ToString(),
            "greet",
            new() { PrefixMode = "Default", CustomPrefix = "" }
        );

        CommandDto fetched = (await sut.GetAsync(Channel.ToString(), "greet")).Value;
        fetched.PrefixMode.Should().Be("Default");
        fetched.CustomPrefix.Should().BeNull();
    }

    // ─── S042: save-time template helper validation ───────────────────────────

    [Fact]
    public async Task Create_WithAnUnknownTemplateHelper_IsRejectedNamingTheKeyAndPersistsNothing()
    {
        (CommandService sut, RecordingEventBus bus) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "broken", TemplateResponse = "Hi {user.nmae}!" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("user.nmae");
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithAllValidTemplateHelpers_Succeeds()
    {
        (CommandService sut, _) = Build();

        Result<CommandDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "greetvalid", TemplateResponse = "Hi {user.name}, arg was {args.1}!" }
        );

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_WithAnUnknownTemplateHelperInAVariation_IsRejectedAndLeavesTheCommandUnchanged()
    {
        (CommandService sut, RecordingEventBus bus) = Build();
        await sut.CreateAsync(Channel.ToString(), Req("varfix"));
        bus.Published.Clear();

        Result<CommandDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            "varfix",
            new() { TemplateResponses = ["still fine", "Hi {totally.bogus.helper}"] }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("totally.bogus.helper");
        bus.Published.Should().BeEmpty();

        CommandDto unchanged = (await sut.GetAsync(Channel.ToString(), "varfix")).Value;
        unchanged.TemplateResponse.Should().Be("hi there");
    }
}
