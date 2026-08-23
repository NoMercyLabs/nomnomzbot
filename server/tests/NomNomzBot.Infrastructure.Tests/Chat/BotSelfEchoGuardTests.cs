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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves <see cref="BotSelfEchoGuard"/>'s own DB-backed identity resolution against the REAL
/// <see cref="AppDbContext"/> (the Kick/YouTube ingest tests stand in a hand-written double instead,
/// because the fake <c>AuthDbContext</c> they share does not model <c>BotAccount</c>/
/// <c>ChannelBotAuthorization</c>): a connected per-channel custom bot wins over the shared bot, the
/// shared bot applies only when no custom bot is connected, no bot at all resolves to "no identity" (the
/// self-host case), and the cache is evicted on connect/disconnect rather than serving a stale answer.
/// </summary>
public sealed class BotSelfEchoGuardTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("0199c000-0000-7000-8000-0000000000c1");
    private static readonly Guid TenantB = Guid.Parse("0199c000-0000-7000-8000-0000000000c2");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public BotSelfEchoGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        ServiceCollection services = new();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<NomNomzBot.Application.Abstractions.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<AppDbContext>()
        );
        _provider = services.BuildServiceProvider();

        using IServiceScope scope = _provider.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        // This suite only needs Channel/BotAccount/ChannelBotAuthorization graphs; skip the FK-enforced
        // User row a Channel normally requires (the same posture AuthTestContext takes for partial graphs).
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.AddRange(
            new Channel
            {
                Id = TenantA,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tenant-a-ext",
                Name = "tenant-a",
                NameNormalized = "tenant-a",
            },
            new Channel
            {
                Id = TenantB,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tenant-b-ext",
                Name = "tenant-b",
                NameNormalized = "tenant-b",
            }
        );
        db.SaveChanges();
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private BotSelfEchoGuard NewGuard() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>());

    [Fact]
    public async Task No_bot_connected_never_suppresses_an_unmarked_line()
    {
        BotSelfEchoGuard guard = NewGuard();

        bool suppressed = await guard.ShouldSuppressAsync(
            TenantA,
            AuthEnums.Platform.Twitch,
            "555",
            "!cmd"
        );

        suppressed
            .Should()
            .BeFalse("self-host with no dedicated bot must honour the streamer's own line");
    }

    [Fact]
    public async Task The_shared_bots_line_is_suppressed_for_a_tenant_with_no_custom_bot()
    {
        await using AppDbContext db = NewDbContext();
        db.BotAccounts.Add(
            new()
            {
                IdentityType = AuthEnums.BotIdentityType.Shared,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = "shared-bot-1",
                BotUsername = "NomNomzBot",
                IsActive = true,
                ConnectionId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();
        BotSelfEchoGuard guard = NewGuard();

        bool suppressed = await guard.ShouldSuppressAsync(
            TenantA,
            AuthEnums.Platform.Twitch,
            "shared-bot-1",
            "any text"
        );

        suppressed
            .Should()
            .BeTrue("the connected shared bot applies to every tenant without a custom bot");
    }

    [Fact]
    public async Task A_connected_custom_bot_wins_over_the_shared_bot_for_its_own_tenant()
    {
        await using AppDbContext db = NewDbContext();
        db.BotAccounts.Add(
            new()
            {
                IdentityType = AuthEnums.BotIdentityType.Shared,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = "shared-bot-1",
                BotUsername = "NomNomzBot",
                IsActive = true,
                ConnectionId = Guid.NewGuid(),
            }
        );
        BotAccount customBot = new()
        {
            IdentityType = AuthEnums.BotIdentityType.Custom,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = "custom-bot-1",
            BotUsername = "StreamerBot",
            IsActive = true,
            ConnectionId = Guid.NewGuid(),
        };
        db.BotAccounts.Add(customBot);
        db.ChannelBotAuthorizations.Add(
            new()
            {
                BroadcasterId = TenantA,
                BotAccountId = customBot.Id,
                AuthorizedAt = DateTime.UtcNow,
                IsActive = true,
            }
        );
        await db.SaveChangesAsync();
        BotSelfEchoGuard guard = NewGuard();

        (await guard.ShouldSuppressAsync(TenantA, AuthEnums.Platform.Twitch, "custom-bot-1", "x"))
            .Should()
            .BeTrue("the tenant's own custom bot wins");
        (await guard.ShouldSuppressAsync(TenantA, AuthEnums.Platform.Twitch, "shared-bot-1", "x"))
            .Should()
            .BeFalse("the shared bot's identity does not apply once a custom bot is connected");
        (await guard.ShouldSuppressAsync(TenantB, AuthEnums.Platform.Twitch, "shared-bot-1", "x"))
            .Should()
            .BeTrue("a DIFFERENT tenant with no custom bot still falls back to the shared bot");
    }

    [Fact]
    public async Task The_emitted_line_marker_suppresses_regardless_of_sender_identity()
    {
        BotSelfEchoGuard guard = NewGuard();

        bool suppressed = await guard.ShouldSuppressAsync(
            TenantA,
            AuthEnums.Platform.Twitch,
            "555", // the streamer's own account, self-host, no dedicated bot
            BotEmittedLine.Marker + "Try !cmd again"
        );

        suppressed.Should().BeTrue("the marker alone identifies a bot-emitted line on any account");
    }

    [Fact]
    public async Task Disconnecting_a_custom_bot_evicts_its_tenants_cached_identity()
    {
        await using AppDbContext db = NewDbContext();
        BotAccount customBot = new()
        {
            IdentityType = AuthEnums.BotIdentityType.Custom,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = "custom-bot-1",
            BotUsername = "StreamerBot",
            IsActive = true,
            ConnectionId = Guid.NewGuid(),
        };
        db.BotAccounts.Add(customBot);
        ChannelBotAuthorization authorization = new()
        {
            BroadcasterId = TenantA,
            BotAccountId = customBot.Id,
            AuthorizedAt = DateTime.UtcNow,
            IsActive = true,
        };
        db.ChannelBotAuthorizations.Add(authorization);
        await db.SaveChangesAsync();
        BotSelfEchoGuard guard = NewGuard();

        // Warms the per-tenant cache with "custom-bot-1 is the bot here".
        (await guard.ShouldSuppressAsync(TenantA, AuthEnums.Platform.Twitch, "custom-bot-1", "x"))
            .Should()
            .BeTrue();

        // The bot disconnects — a real caller would flip IsActive and publish
        // BotAccountDisconnectedEvent; here the cache eviction is exercised directly, the same call
        // BotIdentityCacheInvalidationHandler makes on that event.
        authorization.IsActive = false;
        await db.SaveChangesAsync();
        ((IBotIdentityCacheInvalidation)guard).InvalidateTenant(TenantA);

        (await guard.ShouldSuppressAsync(TenantA, AuthEnums.Platform.Twitch, "custom-bot-1", "x"))
            .Should()
            .BeFalse("the stale cached identity must not keep suppressing after disconnect");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
