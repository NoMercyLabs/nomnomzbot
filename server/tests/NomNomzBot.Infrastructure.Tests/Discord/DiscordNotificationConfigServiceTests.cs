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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Infrastructure.Discord;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the notification-config service (discord.md §3.2). Proves the persisted rule shape
/// (including the <c>[VC:JSON]</c> embed round-trip), the <c>(GuildConnectionId, TriggerType)</c> uniqueness,
/// milestone-field validation, and the template preview rendering.
/// </summary>
public sealed class DiscordNotificationConfigServiceTests
{
    [Fact]
    public async Task CreateConfigAsync_PersistsFullRule_WithEmbedJsonRoundTrip()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        DiscordEmbedDto embed = new(
            "Live now",
            "Come watch",
            "#9146FF",
            "https://img/thumb.png",
            null,
            "footer text",
            [new("Game", "Just Chatting", true)]
        );

        DiscordNotificationConfigDto created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationConfigService service = NewService(db);
            Result<DiscordNotificationConfigDto> result = await service.CreateConfigAsync(
                channel,
                connectionId,
                new("go_live", true, "chan-123", null, "{broadcaster} live", embed, null, null)
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            created = result.Value;
        }

        // The persisted row carries the full shape, and the embed JSON survives a DB round-trip via Newtonsoft.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationConfig stored = await db.DiscordNotificationConfigs.SingleAsync();
            stored.Id.Should().Be(created.Id);
            stored.TriggerType.Should().Be("go_live");
            stored.TargetChannelId.Should().Be("chan-123");
            stored.MessageTemplate.Should().Be("{broadcaster} live");
            stored.ConfigSchemaVersion.Should().Be(1);
            stored.EmbedConfig.Should().NotBeNull();
            stored.EmbedConfig!.Title.Should().Be("Live now");
            stored.EmbedConfig!.Color.Should().Be("#9146FF");
            stored.EmbedConfig!.Fields.Should().ContainSingle();
            stored.EmbedConfig!.Fields![0].Name.Should().Be("Game");
        }
    }

    [Fact]
    public async Task CreateConfigAsync_DuplicateTrigger_IsAlreadyExists()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        CreateDiscordNotificationConfigRequest request = new(
            "go_live",
            true,
            "chan-1",
            null,
            "msg",
            null,
            null,
            null
        );

        await using (DiscordTestDbContext db = database.NewContext())
            (await NewService(db).CreateConfigAsync(channel, connectionId, request))
                .IsSuccess.Should()
                .BeTrue();

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationConfigDto> second = await NewService(db)
                .CreateConfigAsync(channel, connectionId, request);
            second.IsFailure.Should().BeTrue();
            second.ErrorCode.Should().Be("ALREADY_EXISTS");
        }
    }

    [Fact]
    public async Task CreateConfigAsync_MilestoneTriggerWithoutFields_FailsValidation()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        await using DiscordTestDbContext db = database.NewContext();
        Result<DiscordNotificationConfigDto> result = await NewService(db)
            .CreateConfigAsync(
                channel,
                connectionId,
                new(
                    "milestone",
                    true,
                    "chan-1",
                    null,
                    "msg",
                    null,
                    MilestoneType: null, // missing — required for milestone
                    MilestoneThreshold: null
                )
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task PreviewAsync_RendersTemplateAgainstSampleData_WithoutPosting()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);

        Guid configId;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationConfigDto> created = await NewService(db)
                .CreateConfigAsync(
                    channel,
                    connectionId,
                    new(
                        "go_live",
                        true,
                        "chan-1",
                        null,
                        "{channel.name} playing {channel.game}",
                        null,
                        null,
                        null
                    )
                );
            configId = created.Value.Id;
        }

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordNotificationPreviewDto> preview = await NewService(db)
                .PreviewAsync(channel, configId);
            preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
            // The sample data filled the placeholders — no {{...}} left.
            preview.Value.RenderedContent.Should().Be("SampleStreamer playing Just Chatting");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordNotificationConfigService NewService(DiscordTestDbContext db) =>
        new(
            db,
            new DiscordTestUnitOfWork(db),
            DiscordTemplateTestSupport.CreateResolver(),
            DiscordTemplateTestSupport.CreateValidator()
        );
}
