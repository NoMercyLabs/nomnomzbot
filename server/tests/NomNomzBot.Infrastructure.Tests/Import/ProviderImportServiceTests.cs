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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Import.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Import;
using NomNomzBot.Infrastructure.Quotes;
using NomNomzBot.Infrastructure.Tests.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using DomainCommand = NomNomzBot.Domain.Commands.Entities.Command;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Import;

/// <summary>
/// Behavior tests for the StreamElements import. Each proves the consequence of an import — the rows that land
/// with their mapped fields, the duplicates that are skipped and counted, and the access-level → role-ladder
/// mapping — by driving the real command / quote / timer services over a real SQLite database, then reading the
/// persisted state back from a fresh context.
/// </summary>
public sealed class ProviderImportServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero)
    );

    private static ProviderImportService NewService(ImportTestDbContext db)
    {
        RecordingEventBus bus = new();
        CommandService commands = new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            bus,
            TestTiers.Unlimited()
        );
        QuoteService quotes = new(
            db,
            new TenantSequenceAllocator(db),
            new ImportTestUnitOfWork(db),
            bus,
            Clock
        );
        TimerManagementService timers = new(db, bus, TestTiers.Unlimited());
        return new ProviderImportService(db, commands, quotes, timers);
    }

    private static async Task<Guid> SeedChannelAsync(ImportSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using ImportTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    [Fact]
    public async Task Import_persists_commands_quotes_and_timers_with_mapped_fields()
    {
        using ImportSqliteTestDatabase database = ImportSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        StreamElementsExport export = new()
        {
            Commands =
            [
                new SeCommand
                {
                    Command = "!discord",
                    Response = "Join at discord.gg/nomnomz",
                    AccessLevel = 0,
                    Cooldown = 30,
                    Aliases = ["!disc"],
                },
                new SeCommand
                {
                    Command = "!so",
                    Response = "Check out {{args.1}}",
                    AccessLevel = 500,
                    UserCooldown = 15,
                },
            ],
            Quotes =
            [
                new SeQuote
                {
                    Text = "blame the lag",
                    AddedBy = "Stoney_Eagle",
                    Game = "Just Chatting",
                },
                new SeQuote { Text = "clip it and ship it", AddedBy = "aaoa-dev" },
            ],
            Timers =
            [
                new SeTimer
                {
                    Name = "follow-reminder",
                    Message = "Don't forget to follow!",
                    Interval = 900,
                    ChatLines = 5,
                },
            ],
        };

        ImportSummary summary;
        await using (ImportTestDbContext db = database.NewContext())
        {
            Result<ImportSummary> result = await NewService(db)
                .ImportStreamElementsAsync(channel, export);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            summary = result.Value;
        }

        summary.CommandsImported.Should().Be(2);
        summary.QuotesImported.Should().Be(2);
        summary.TimersImported.Should().Be(1);
        summary.Warnings.Should().BeEmpty();

        // Read the persisted state back from a fresh context — prove the rows landed with the mapped shape.
        await using ImportTestDbContext read = database.NewContext();

        DomainCommand discord = await read.Commands.SingleAsync(c => c.NameNormalized == "discord");
        discord.TemplateResponse.Should().Be("Join at discord.gg/nomnomz");
        discord.MinPermissionLevel.Should().Be(0, "accessLevel 0 → everyone");
        discord.CooldownSeconds.Should().Be(30);
        discord.CooldownPerUser.Should().BeFalse("a global SE cooldown is not per-user");
        discord.Aliases.Should().ContainSingle().Which.Should().Be("disc", "the sigil is stripped");

        DomainCommand shoutout = await read.Commands.SingleAsync(c => c.NameNormalized == "so");
        shoutout.MinPermissionLevel.Should().Be(5, "accessLevel 500 → broadcaster");
        shoutout.CooldownSeconds.Should().Be(15, "the per-user cooldown wins when present");
        shoutout.CooldownPerUser.Should().BeTrue();

        List<NomNomzBot.Domain.Quotes.Entities.Quote> storedQuotes = await read
            .Quotes.OrderBy(q => q.Number)
            .ToListAsync();
        storedQuotes.Should().HaveCount(2);
        storedQuotes[0].Number.Should().Be(1, "quote numbering starts at one");
        storedQuotes[0].Text.Should().Be("blame the lag");
        storedQuotes[0].QuotedDisplayName.Should().Be("Stoney_Eagle");
        storedQuotes[0].ContextGame.Should().Be("Just Chatting");
        storedQuotes[1].Number.Should().Be(2);
        storedQuotes[1].Text.Should().Be("clip it and ship it");

        DomainTimer timer = await read.Timers.SingleAsync();
        timer.Name.Should().Be("follow-reminder");
        timer.Messages.Should().ContainSingle().Which.Should().Be("Don't forget to follow!");
        timer.IntervalMinutes.Should().Be(15, "900 seconds → 15 minutes");
        timer.MinChatActivity.Should().Be(5);
    }

    [Fact]
    public async Task Reimporting_the_same_export_skips_duplicate_command_quote_and_timer()
    {
        using ImportSqliteTestDatabase database = ImportSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        StreamElementsExport export = new()
        {
            Commands =
            [
                new SeCommand
                {
                    Command = "!hello",
                    Response = "hi",
                    AccessLevel = 0,
                },
            ],
            Quotes = [new SeQuote { Text = "first blood", AddedBy = "Stoney_Eagle" }],
            Timers =
            [
                new SeTimer
                {
                    Name = "promo",
                    Message = "Subscribe!",
                    Interval = 600,
                },
            ],
        };

        // First import establishes the rows.
        await using (ImportTestDbContext db = database.NewContext())
        {
            ImportSummary first = (
                await NewService(db).ImportStreamElementsAsync(channel, export)
            ).Value;
            first.CommandsImported.Should().Be(1);
            first.QuotesImported.Should().Be(1);
            first.TimersImported.Should().Be(1);
        }

        // Re-importing the identical export imports nothing new — every entity is a duplicate, skipped + counted.
        ImportSummary second;
        await using (ImportTestDbContext db = database.NewContext())
        {
            second = (await NewService(db).ImportStreamElementsAsync(channel, export)).Value;
        }

        second.CommandsImported.Should().Be(0);
        second.CommandsSkipped.Should().Be(1, "the command name already exists");
        second.QuotesImported.Should().Be(0);
        second.QuotesSkipped.Should().Be(1, "the quote text already exists");
        second.TimersImported.Should().Be(0);
        second.TimersSkipped.Should().Be(1, "the timer name already exists");

        // The skips did not create second copies.
        await using ImportTestDbContext read = database.NewContext();
        (await read.Commands.CountAsync()).Should().Be(1);
        (await read.Quotes.CountAsync()).Should().Be(1);
        (await read.Timers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Two_identical_quote_texts_in_one_payload_import_once()
    {
        using ImportSqliteTestDatabase database = ImportSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        StreamElementsExport export = new()
        {
            Quotes =
            [
                new SeQuote { Text = "GG well played" },
                new SeQuote { Text = "gg well played" },
            ],
        };

        ImportSummary summary;
        await using (ImportTestDbContext db = database.NewContext())
        {
            summary = (await NewService(db).ImportStreamElementsAsync(channel, export)).Value;
        }

        summary.QuotesImported.Should().Be(1, "the second is a case-insensitive duplicate");
        summary.QuotesSkipped.Should().Be(1);

        await using ImportTestDbContext read = database.NewContext();
        (await read.Quotes.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(null, 0)] // unspecified → everyone
    [InlineData(0, 0)] // everyone
    [InlineData(100, 2)] // subscriber
    [InlineData(250, 3)] // vip (SE "regular")
    [InlineData(400, 4)] // moderator band
    [InlineData(500, 5)] // broadcaster
    [InlineData(1000, 5)] // higher owner scale clamps to broadcaster
    public async Task Access_level_maps_onto_the_role_ladder(int? accessLevel, int expectedLevel)
    {
        using ImportSqliteTestDatabase database = ImportSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        StreamElementsExport export = new()
        {
            Commands =
            [
                new SeCommand
                {
                    Command = "!lvl",
                    Response = "x",
                    AccessLevel = accessLevel,
                },
            ],
        };

        await using (ImportTestDbContext db = database.NewContext())
        {
            (await NewService(db).ImportStreamElementsAsync(channel, export))
                .Value.CommandsImported.Should()
                .Be(1);
        }

        await using ImportTestDbContext read = database.NewContext();
        DomainCommand command = await read.Commands.SingleAsync();
        command.MinPermissionLevel.Should().Be(expectedLevel);
    }
}
