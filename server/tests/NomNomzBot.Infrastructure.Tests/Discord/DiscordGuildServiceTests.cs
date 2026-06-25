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
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Behavior tests for the guild link service (discord.md §3.1). Each proves a consequence: the connection row
/// that lands, the bot token handed to the VAULT (never a plaintext column), the both-opt-in gate, and the
/// linked/unlinked events emitted on consent transitions.
/// </summary>
public sealed class DiscordGuildServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 22, 12, 0, 0, TimeSpan.Zero)
    );

    [Fact]
    public async Task UpsertFromOAuthAsync_CreatesConnection_AndVaultsTheBotToken()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        RecordingVault vault = new();
        RecordingEventBus bus = new();

        DiscordGuildConnectionDto created;
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildService service = NewService(db, vault, bus);
            Result<DiscordGuildConnectionDto> result = await service.UpsertFromOAuthAsync(
                channel,
                new DiscordGuildOAuthResult(
                    "guild-123",
                    "Cool Server",
                    "bot-access-token",
                    "bot-refresh-token",
                    Clock.GetUtcNow().UtcDateTime.AddDays(7),
                    ["bot", "guilds"],
                    "installer-discord-id"
                )
            );

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            created = result.Value;
        }

        // The connection row landed with the guild, bot-installed, and server-approved (the install implies it).
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildConnection stored = await db.DiscordGuildConnections.SingleAsync(c =>
                c.BroadcasterId == channel
            );
            stored.Id.Should().Be(created.Id);
            stored.GuildId.Should().Be("guild-123");
            stored.GuildName.Should().Be("Cool Server");
            stored.BotInstalled.Should().BeTrue();
            stored.ServerConsentStatus.Should().Be("approved");
            stored.ApprovedByDiscordUserId.Should().Be("installer-discord-id");
        }

        // The bot token went to the VAULT (UpsertConnection then StoreTokens) — NOT a plaintext column on the
        // connection row. This is the crypto-shred-ready custody guarantee.
        vault.UpsertedProviders.Should().ContainSingle().Which.Should().Be("discord");
        vault.StoredTokens.Should().ContainSingle();
        vault.StoredTokens[0].AccessToken.Should().Be("bot-access-token");
        vault.StoredTokens[0].RefreshToken.Should().Be("bot-refresh-token");
    }

    [Fact]
    public async Task SetStreamerEnabled_OnApprovedConnection_ReachesBothOptIn_AndPublishesLinked()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "approved",
            streamerEnabled: false
        );
        RecordingEventBus bus = new();

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildService service = NewService(db, new RecordingVault(), bus);
            Result result = await service.SetStreamerEnabledAsync(channel, connectionId, true);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        // Crossing into both-opt-in (approved + enabled) publishes the linked event with the guild details.
        bus.Published.OfType<DiscordGuildLinkedEvent>().Should().ContainSingle();
        DiscordGuildLinkedEvent linked = bus.Published.OfType<DiscordGuildLinkedEvent>().Single();
        linked.GuildConnectionId.Should().Be(connectionId);
        linked.BroadcasterId.Should().Be(channel);

        // And the gate now reports active.
        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildService service = NewService(
                db,
                new RecordingVault(),
                new RecordingEventBus()
            );
            Result<bool> active = await service.IsLinkActiveAsync(channel, connectionId);
            active.Value.Should().BeTrue();
        }
    }

    [Fact]
    public async Task SetStreamerEnabled_False_BreaksBothOptIn_AndPublishesUnlinked()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "approved",
            streamerEnabled: true
        );
        RecordingEventBus bus = new();

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildService service = NewService(db, new RecordingVault(), bus);
        await service.SetStreamerEnabledAsync(channel, connectionId, false);

        DiscordGuildUnlinkedEvent unlinked = bus
            .Published.OfType<DiscordGuildUnlinkedEvent>()
            .Single();
        unlinked.Reason.Should().Be("streamer_disabled");
        unlinked.GuildConnectionId.Should().Be(connectionId);
    }

    [Fact]
    public async Task DisconnectAsync_SoftDeletesConnection_RevokesVaultToken_AndPublishesUnlinked()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid connectionId = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "approved",
            streamerEnabled: true,
            guildId: "guild-xyz"
        );
        // The vault connection the disconnect must revoke.
        await SeedVaultConnectionAsync(database, channel, "guild-xyz");
        RecordingVault vault = new();
        RecordingEventBus bus = new();

        await using (DiscordTestDbContext db = database.NewContext())
        {
            DiscordGuildService service = NewService(db, vault, bus);
            Result result = await service.DisconnectAsync(channel, connectionId);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        // The connection is soft-deleted (gone from filtered reads, but the row + DeletedAt survive).
        await using (DiscordTestDbContext db = database.NewContext())
        {
            bool visible = await db.DiscordGuildConnections.AnyAsync(c => c.Id == connectionId);
            visible.Should().BeFalse();
            DiscordGuildConnection stored = await db
                .DiscordGuildConnections.IgnoreQueryFilters()
                .SingleAsync(c => c.Id == connectionId);
            stored.DeletedAt.Should().NotBeNull();
        }

        // The vaulted bot token was revoked (crypto-shred path), and the unlinked event fired.
        vault.RevokedReasons.Should().ContainSingle().Which.Should().Be("discord_disconnected");
        bus.Published.OfType<DiscordGuildUnlinkedEvent>()
            .Single()
            .Reason.Should()
            .Be("disconnected");
    }

    [Fact]
    public async Task IsLinkActiveAsync_RequiresBothApprovedAndEnabled()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid pendingOnly = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "pending",
            streamerEnabled: true,
            guildId: "g1"
        );
        Guid enabledNotApproved = await SeedConnectionAsync(
            database,
            channel,
            serverConsent: "pending",
            streamerEnabled: true,
            guildId: "g2"
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordGuildService service = NewService(db, new RecordingVault(), new RecordingEventBus());

        (await service.IsLinkActiveAsync(channel, pendingOnly)).Value.Should().BeFalse();
        (await service.IsLinkActiveAsync(channel, enabledNotApproved)).Value.Should().BeFalse();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordGuildService NewService(
        DiscordTestDbContext db,
        RecordingVault vault,
        RecordingEventBus bus
    ) => new(db, vault, new DiscordTestUnitOfWork(db), bus, Clock);

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

    private static async Task<Guid> SeedConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        string serverConsent,
        bool streamerEnabled,
        string guildId = "guild-1"
    )
    {
        Guid id = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.DiscordGuildConnections.Add(
            new DiscordGuildConnection
            {
                Id = id,
                BroadcasterId = channel,
                GuildId = guildId,
                GuildName = "Server",
                BotInstalled = true,
                ServerConsentStatus = serverConsent,
                StreamerEnabled = streamerEnabled,
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedVaultConnectionAsync(
        DiscordSqliteTestDatabase database,
        Guid channel,
        string guildId
    )
    {
        await using DiscordTestDbContext db = database.NewContext();
        db.IntegrationConnections.Add(
            new NomNomzBot.Domain.Integrations.Entities.IntegrationConnection
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channel,
                Provider = "discord",
                ProviderAccountId = guildId,
                Status = "connected",
            }
        );
        await db.SaveChangesAsync();
    }
}
