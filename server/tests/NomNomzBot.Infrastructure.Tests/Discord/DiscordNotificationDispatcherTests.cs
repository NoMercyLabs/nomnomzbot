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

    // ─── Personal DM fan-out (discord-notifications: decided 2026-07-17) ────

    [Fact]
    public async Task DispatchAsync_DmEnabledRole_DmsEveryOptedInMember_AndCachesDmChannel()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: true);
        await SeedOptInAsync(database, channel, roleId, "member-1");
        await SeedOptInAsync(database, channel, roleId, "member-2");
        await SeedConfigAsync(database, channel, connectionId, "{{title}}", "chan-1", roleId);
        RecordingGateway gateway = new();

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordDispatchOutcomeDto> result = await NewDispatcher(
                    db,
                    gateway,
                    new RecordingEventBus()
                )
                .DispatchAsync(
                    new DiscordDispatchRequest(
                        channel,
                        "go_live",
                        "go_live:s1",
                        null,
                        new Dictionary<string, string> { ["title"] = "live now" }
                    )
                );
            result.Value.Status.Should().Be("sent");
        }

        // Channel post + one DM per opted-in member, each carrying the SAME rendered content, no ping.
        gateway.Posts.Should().HaveCount(3);
        gateway.Posts[0].ChannelId.Should().Be("chan-1");
        gateway.DmOpens.Should().Equal("member-1", "member-2");
        gateway.Posts[1].ChannelId.Should().Be("dm-member-1");
        gateway.Posts[1].Message.Content.Should().Be("live now");
        gateway.Posts[1].Message.PingRoleId.Should().BeNull();
        gateway.Posts[2].ChannelId.Should().Be("dm-member-2");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            // Each DM is its own append-only dispatch row keyed {base}:dm:{memberId}.
            List<DiscordNotificationDispatch> dmRows = await db
                .DiscordNotificationDispatches.Where(d => d.DedupeKey.Contains(":dm:"))
                .OrderBy(d => d.DedupeKey)
                .ToListAsync();
            dmRows.Should().HaveCount(2);
            dmRows[0].DedupeKey.Should().Be("go_live:s1:dm:member-1");
            dmRows[0].Status.Should().Be("sent");
            dmRows[0].PostedMessageId.Should().Be("posted-msg-id");
            dmRows[1].DedupeKey.Should().Be("go_live:s1:dm:member-2");

            // The opened DM channel is cached on the opt-in row for the next go-live.
            List<string?> cached = await db
                .DiscordMemberOptIns.OrderBy(o => o.DiscordMemberId)
                .Select(o => o.DmChannelId)
                .ToListAsync();
            cached.Should().Equal("dm-member-1", "dm-member-2");
        }
    }

    [Fact]
    public async Task DispatchAsync_DmFanOut_SkipsOptedOutMembers()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: true);
        await SeedOptInAsync(database, channel, roleId, "active-member");
        await SeedOptInAsync(database, channel, roleId, "gone-member", optedOut: true);
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1", roleId);
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        await NewDispatcher(db, gateway, new RecordingEventBus())
            .DispatchAsync(
                new DiscordDispatchRequest(
                    channel,
                    "go_live",
                    "go_live:s2",
                    null,
                    new Dictionary<string, string>()
                )
            );

        // Only the active member is DMed — the opted-out row is history, not a recipient.
        gateway.DmOpens.Should().Equal("active-member");
        gateway.Posts.Should().HaveCount(2); // channel + one DM
    }

    [Fact]
    public async Task DispatchAsync_DmDisabledRole_PostsToChannelOnly()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: false);
        await SeedOptInAsync(database, channel, roleId, "member-1");
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1", roleId);
        RecordingGateway gateway = new();

        await using DiscordTestDbContext db = database.NewContext();
        await NewDispatcher(db, gateway, new RecordingEventBus())
            .DispatchAsync(
                new DiscordDispatchRequest(
                    channel,
                    "go_live",
                    "go_live:s3",
                    null,
                    new Dictionary<string, string>()
                )
            );

        gateway.DmOpens.Should().BeEmpty();
        gateway.Posts.Should().ContainSingle(); // the channel post — no DMs without DmEnabled
    }

    [Fact]
    public async Task DispatchAsync_ClosedDm_FailsThatMemberAndContinuesTheRest()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: true);
        await SeedOptInAsync(database, channel, roleId, "blocked-member");
        await SeedOptInAsync(database, channel, roleId, "open-member");
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1", roleId);
        RecordingGateway gateway = new();
        // Discord 50007: cannot send messages to this user — surfaces at send time on the DM channel.
        gateway.PostResultsByChannel["dm-blocked-member"] = Result.Failure<string>(
            "Cannot send messages to this user",
            "DISCORD_ERROR"
        );

        await using (DiscordTestDbContext db = database.NewContext())
        {
            Result<DiscordDispatchOutcomeDto> result = await NewDispatcher(
                    db,
                    gateway,
                    new RecordingEventBus()
                )
                .DispatchAsync(
                    new DiscordDispatchRequest(
                        channel,
                        "go_live",
                        "go_live:s4",
                        null,
                        new Dictionary<string, string>()
                    )
                );
            result.Value.Status.Should().Be("sent"); // the channel outcome is untouched by DM failures
        }

        // Both members were attempted — the closed DM did not stop the fan-out.
        gateway.DmOpens.Should().Equal("blocked-member", "open-member");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationDispatch failedRow =
                await db.DiscordNotificationDispatches.SingleAsync(d =>
                    d.DedupeKey == "go_live:s4:dm:blocked-member"
                );
            failedRow.Status.Should().Be("failed");
            failedRow.Error.Should().Be("Cannot send messages to this user");

            DiscordNotificationDispatch sentRow =
                await db.DiscordNotificationDispatches.SingleAsync(d =>
                    d.DedupeKey == "go_live:s4:dm:open-member"
                );
            sentRow.Status.Should().Be("sent");
        }
    }

    [Fact]
    public async Task DispatchAsync_MemberAlreadyDmed_IsAPerMemberNoOp()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: true);
        await SeedOptInAsync(database, channel, roleId, "already-dmed");
        await SeedOptInAsync(database, channel, roleId, "fresh-member");
        Guid configId = await SeedConfigAsync(
            database,
            channel,
            connectionId,
            "live!",
            "chan-1",
            roleId
        );

        // A prior partial run already DMed one member — its dedupe row exists, the channel row does not.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            db.DiscordNotificationDispatches.Add(
                new DiscordNotificationDispatch
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = channel,
                    NotificationConfigId = configId,
                    TriggerType = "go_live",
                    DedupeKey = "go_live:s5:dm:already-dmed",
                    Status = "sent",
                    PostedMessageId = "earlier-dm-msg",
                    DispatchedAt = Clock.GetUtcNow().UtcDateTime,
                }
            );
            await db.SaveChangesAsync();
        }

        RecordingGateway gateway = new();
        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewDispatcher(db, gateway, new RecordingEventBus())
                .DispatchAsync(
                    new DiscordDispatchRequest(
                        channel,
                        "go_live",
                        "go_live:s5",
                        null,
                        new Dictionary<string, string>()
                    )
                );
        }

        // Only the fresh member was DMed — the unique dedupe row made the repeat a no-op.
        gateway.DmOpens.Should().Equal("fresh-member");

        await using (DiscordTestDbContext dbCheck = database.NewContext())
        {
            int rowsForAlreadyDmed = await dbCheck.DiscordNotificationDispatches.CountAsync(d =>
                d.DedupeKey == "go_live:s5:dm:already-dmed"
            );
            rowsForAlreadyDmed.Should().Be(1); // still just the original row
        }
    }

    [Fact]
    public async Task DispatchAsync_CachedDmChannel_SkipsOpenCall_AndOpenFailureContinues()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedActiveConnectionAsync(database, channel);
        Guid roleId = await SeedNotifyRoleAsync(database, channel, connectionId, dmEnabled: true);
        await SeedOptInAsync(database, channel, roleId, "uncached-member");
        await SeedOptInAsync(
            database,
            channel,
            roleId,
            "cached-member",
            dmChannelId: "dm-cached-77"
        );
        await SeedConfigAsync(database, channel, connectionId, "live!", "chan-1", roleId);
        RecordingGateway gateway = new()
        {
            // Every open fails (e.g. Discord API hiccup) — only the uncached member needs one.
            NextDmOpenResult = Result.Failure<string>("open failed", "DISCORD_ERROR"),
        };

        await using (DiscordTestDbContext db = database.NewContext())
        {
            await NewDispatcher(db, gateway, new RecordingEventBus())
                .DispatchAsync(
                    new DiscordDispatchRequest(
                        channel,
                        "go_live",
                        "go_live:s6",
                        null,
                        new Dictionary<string, string>()
                    )
                );
        }

        // The cached member never triggers an open; the uncached member's failed open didn't block them.
        gateway.DmOpens.Should().Equal("uncached-member");
        gateway.Posts.Should().HaveCount(2); // channel + the cached member's DM
        gateway.Posts[1].ChannelId.Should().Be("dm-cached-77");

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordNotificationDispatch failedOpen =
                await db.DiscordNotificationDispatches.SingleAsync(d =>
                    d.DedupeKey == "go_live:s6:dm:uncached-member"
                );
            failedOpen.Status.Should().Be("failed");
            failedOpen.Error.Should().Be("open failed");

            DiscordNotificationDispatch cachedSent =
                await db.DiscordNotificationDispatches.SingleAsync(d =>
                    d.DedupeKey == "go_live:s6:dm:cached-member"
                );
            cachedSent.Status.Should().Be("sent");
        }
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

    private static async Task<Guid> SeedConfigAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        Guid connectionId,
        string template,
        string targetChannel,
        Guid? pingRoleId = null
    )
    {
        Guid configId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordNotificationConfigs.Add(
            new DiscordNotificationConfig
            {
                Id = configId,
                BroadcasterId = channel,
                GuildConnectionId = connectionId,
                TriggerType = "go_live",
                Enabled = true,
                TargetChannelId = targetChannel,
                MessageTemplate = template,
                PingRoleId = pingRoleId,
                ConfigSchemaVersion = 1,
            }
        );
        await db.SaveChangesAsync();
        return configId;
    }

    private static async Task<Guid> SeedNotifyRoleAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        Guid connectionId,
        bool dmEnabled
    )
    {
        Guid roleId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordNotificationRoles.Add(
            new DiscordNotificationRole
            {
                Id = roleId,
                BroadcasterId = channel,
                GuildConnectionId = connectionId,
                DiscordRoleId = "discord-role-1",
                RoleName = "Notify Squad",
                SelfAssignEnabled = true,
                DmEnabled = dmEnabled,
            }
        );
        await db.SaveChangesAsync();
        return roleId;
    }

    private static async Task SeedOptInAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        Guid roleId,
        string memberId,
        bool optedOut = false,
        string? dmChannelId = null
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordMemberOptIns.Add(
            new DiscordMemberOptIn
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channel,
                NotificationRoleId = roleId,
                DiscordMemberId = memberId,
                OptInSource = "button",
                OptedInAt = Clock.GetUtcNow().UtcDateTime,
                OptedOutAt = optedOut ? Clock.GetUtcNow().UtcDateTime : null,
                DmChannelId = dmChannelId,
            }
        );
        await db.SaveChangesAsync();
    }
}
