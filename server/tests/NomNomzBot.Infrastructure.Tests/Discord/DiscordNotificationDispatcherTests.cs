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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the dispatch + dedupe path (discord.md §3.4). Proves the template renders, the post lands
/// on the configured channel through the gateway, the unique <c>(NotificationConfigId, DedupeKey)</c> insert
/// dedupes a second identical trigger to <c>skipped_dupe</c> (one real post), and an inactive link skips.
/// </summary>
public sealed class DiscordNotificationDispatcherTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 18, 0, 0, TimeSpan.Zero)
    );

    [Fact]
    public async Task DispatchAsync_RendersTemplate_PostsToConfiguredChannel_AndLogsSent()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        await SeedConfigAsync(
            database,
            channel,
            connectionId,
            template: "{{broadcaster}} is live: {{title}}",
            targetChannel: "discord-chan-1"
        );
        RecordingGateway gateway = new();
        RecordingEventBus bus = new();

        DiscordDispatchOutcomeDto outcome;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationDispatcher dispatcher = NewDispatcher(db, gateway, bus);
            Result<DiscordDispatchOutcomeDto> result = await dispatcher.DispatchAsync(
                new DiscordDispatchRequest(
                    channel,
                    "go_live",
                    "go_live:stream-1",
                    null,
                    new Dictionary<string, string>
                    {
                        ["broadcaster"] = "Stoney",
                        ["title"] = "blame the lag",
                    }
                )
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            outcome = result.Value;
        }

        outcome.Status.Should().Be("sent");
        outcome.PostedMessageId.Should().Be("posted-msg-id");

        // The post hit the configured Discord channel with the RENDERED content (template applied).
        gateway.Posts.Should().ContainSingle();
        gateway.Posts[0].ChannelId.Should().Be("discord-chan-1");
        gateway.Posts[0].Message.Content.Should().Be("Stoney is live: blame the lag");

        // The dispatch row is recorded as sent with the returned message id.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationDispatch row = await db.DiscordNotificationDispatches.SingleAsync();
            row.Status.Should().Be("sent");
            row.PostedMessageId.Should().Be("posted-msg-id");
            row.DedupeKey.Should().Be("go_live:stream-1");
        }
    }

    [Fact]
    public async Task DispatchAsync_SameDedupeKeyTwice_PostsOnce_SecondIsSkippedDupe()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1");
        RecordingGateway gateway = new();

        DiscordDispatchRequest request = new(
            channel,
            "go_live",
            "go_live:stream-42",
            null,
            new Dictionary<string, string>()
        );

        // First dispatch → sent.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordDispatchOutcomeDto> first = await NewDispatcher(
                    db,
                    gateway,
                    new RecordingEventBus()
                )
                .DispatchAsync(request);
            first.Value.Status.Should().Be("sent");
        }

        // Second dispatch with the SAME dedupe key → skipped_dupe, no second post.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordDispatchOutcomeDto> second = await NewDispatcher(
                    db,
                    gateway,
                    new RecordingEventBus()
                )
                .DispatchAsync(request);
            second.Value.Status.Should().Be("skipped_dupe");
        }

        // Exactly ONE real post reached Discord — the unique index was the guard.
        gateway.Posts.Should().ContainSingle();

        // The 'sent' row for the dedupe key persists (the second attempt did not create a duplicate sent row).
        await using (DiscordTestDbContext db = database.NewContext())
        {
            int sentCount = await db.DiscordNotificationDispatches.CountAsync(d =>
                d.DedupeKey == "go_live:stream-42" && d.Status == "sent"
            );
            sentCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task DispatchAsync_LinkNotActive_SkipsWithoutPosting()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        // Connection exists but is NOT both-opt-in (streamer disabled) → must skip without a post.
        Guid connectionId = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "approved",
            streamerEnabled: false
        );
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1");
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordNotificationDispatcher dispatcher = NewDispatcher(
            db,
            gateway,
            new RecordingEventBus()
        );
        Result<DiscordDispatchOutcomeDto> result = await dispatcher.DispatchAsync(
            new DiscordDispatchRequest(
                channel,
                "go_live",
                "go_live:s1",
                null,
                new Dictionary<string, string>()
            )
        );

        result.Value.Status.Should().Be("skipped");
        gateway.Posts.Should().BeEmpty(); // never posted — the gate held
    }

    [Fact]
    public async Task DispatchAsync_GatewayFailure_RecordsFailedOutcome()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1");
        RecordingGateway gateway = new()
        {
            NextPostResult = Result.Failure<string>("Discord down", "DISCORD_ERROR"),
        };

        await using DiscordTestDbContext db = database.NewContext();
        DiscordNotificationDispatcher dispatcher = NewDispatcher(
            db,
            gateway,
            new RecordingEventBus()
        );
        Result<DiscordDispatchOutcomeDto> result = await dispatcher.DispatchAsync(
            new DiscordDispatchRequest(
                channel,
                "go_live",
                "go_live:s9",
                null,
                new Dictionary<string, string>()
            )
        );

        result.Value.Status.Should().Be("failed");
        result.Value.Error.Should().Be("Discord down");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordNotificationDispatcher NewDispatcher(
        DiscordTestDbContext db,
        RecordingGateway gateway,
        RecordingEventBus bus
    )
    {
        DiscordGuildService guildService = new(
            db,
            new RecordingVault(),
            new DiscordTestUnitOfWork(db),
            new RecordingEventBus(),
            Clock
        );
        return new DiscordNotificationDispatcher(
            db,
            guildService,
            gateway,
            new TemplateEngine(),
            bus,
            Clock
        );
    }

    private static async Task<Guid> SeedChannelAsync(DiscordSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
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

    private static Task<Guid> SeedActiveConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid channel
    ) => SeedConnectionAsync(database, channel, "approved", true);

    private static async Task<Guid> SeedConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        string serverConsent,
        bool streamerEnabled
    )
    {
        Guid id = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = id,
                BroadcasterId = channel,
                GuildId = "guild-1",
                ServerConsentStatus = serverConsent,
                StreamerEnabled = streamerEnabled,
                BotInstalled = true,
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedConfigAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        Guid connectionId,
        string template,
        string targetChannel
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordNotificationConfigs.Add(
            new DiscordNotificationConfig
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channel,
                GuildConnectionId = connectionId,
                TriggerType = "go_live",
                Enabled = true,
                TargetChannelId = targetChannel,
                MessageTemplate = template,
                ConfigSchemaVersion = 1,
            }
        );
        await db.SaveChangesAsync();
    }
}
