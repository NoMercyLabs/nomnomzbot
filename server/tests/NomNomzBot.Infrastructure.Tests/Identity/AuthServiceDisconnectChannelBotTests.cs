// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A channel's "disconnect bot" action must end that channel's own authorization and never touch a token
/// other channels still depend on. Revoking the shared system bot's connection from one channel's action
/// killed chat for every channel (live incident, 2026-10-18).
/// </summary>
public sealed class AuthServiceDisconnectChannelBotTests
{
    [Fact]
    public async Task ChannelUsingTheSharedBot_EndsItsAuthorizationButNeverRevokesTheSharedConnection()
    {
        (
            AuthService service,
            AuthDbContext db,
            IIntegrationTokenVault vault,
            RecordingEventBus bus
        ) = Build();
        Guid connectionId = Guid.NewGuid();
        BotAccount bot = SeedBot(db, AuthEnums.BotIdentityType.Shared, connectionId);
        Guid channelId = Guid.NewGuid();
        SeedAuthorization(db, channelId, bot.Id);

        Result result = await service.DisconnectChannelBotAsync(channelId);

        result.IsSuccess.Should().BeTrue();
        await vault
            .DidNotReceive()
            .RevokeConnectionAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await AssertAuthorizationEnded(db, channelId);
        bus.Published.OfType<BotAccountDisconnectedEvent>()
            .Should()
            .ContainSingle(e => e.BroadcasterId == channelId && e.BotAccountId == bot.Id);
    }

    [Fact]
    public async Task CustomBotUsedOnlyByThisChannel_IsRevoked()
    {
        (
            AuthService service,
            AuthDbContext db,
            IIntegrationTokenVault vault,
            RecordingEventBus bus
        ) = Build();
        Guid connectionId = Guid.NewGuid();
        BotAccount bot = SeedBot(db, AuthEnums.BotIdentityType.Custom, connectionId);
        Guid channelId = Guid.NewGuid();
        SeedAuthorization(db, channelId, bot.Id);

        Result result = await service.DisconnectChannelBotAsync(channelId);

        result.IsSuccess.Should().BeTrue();
        await vault
            .Received(1)
            .RevokeConnectionAsync(
                connectionId,
                "channel_bot_disconnect",
                Arg.Any<CancellationToken>()
            );
        await AssertAuthorizationEnded(db, channelId);
        bus.Published.OfType<BotAccountDisconnectedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task CustomBotAnotherChannelStillUses_IsNotRevoked()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault, RecordingEventBus _) =
            Build();
        BotAccount bot = SeedBot(db, AuthEnums.BotIdentityType.Custom, Guid.NewGuid());
        Guid channelId = Guid.NewGuid();
        SeedAuthorization(db, channelId, bot.Id);
        SeedAuthorization(db, Guid.NewGuid(), bot.Id);

        Result result = await service.DisconnectChannelBotAsync(channelId);

        result.IsSuccess.Should().BeTrue();
        await vault
            .DidNotReceive()
            .RevokeConnectionAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await AssertAuthorizationEnded(db, channelId);
    }

    private static async Task AssertAuthorizationEnded(AuthDbContext db, Guid channelId)
    {
        ChannelBotAuthorization row = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .SingleAsync(a => a.BroadcasterId == channelId);
        row.IsActive.Should().BeFalse();
        row.DeletedAt.Should().NotBeNull();
    }

    private static BotAccount SeedBot(AuthDbContext db, string identityType, Guid connectionId)
    {
        BotAccount bot = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = identityType,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = Guid.NewGuid().ToString("N")[..10],
            BotUsername =
                identityType == AuthEnums.BotIdentityType.Shared ? "nomz_bot" : "custombot",
            IsActive = true,
            ConnectionId = connectionId,
        };
        db.BotAccounts.Add(bot);
        db.SaveChanges();
        return bot;
    }

    private static void SeedAuthorization(AuthDbContext db, Guid channelId, Guid botAccountId)
    {
        db.ChannelBotAuthorizations.Add(
            new ChannelBotAuthorization
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channelId,
                BotAccountId = botAccountId,
                AuthorizedAt = DateTime.UtcNow,
                IsActive = true,
            }
        );
        db.SaveChanges();
    }

    private static (
        AuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        RecordingEventBus Bus
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Twitch:ClientId"] = "cid" })
            .Build();
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        RecordingEventBus bus = new();

        AuthService service = new(
            db,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchDeviceCodeService>(),
            vault,
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            bus,
            AuthTestBuilder.CredentialsProvider(db, protector, config),
            Substitute.For<IHttpClientFactory>(),
            config,
            new(DeploymentMode.SelfHostLite),
            TimeProvider.System,
            new(),
            Substitute.For<IPlatformOwnerPrincipalMinter>(),
            Substitute.For<IBillingTierService>(),
            NullLogger<AuthService>.Instance
        );

        return (service, db, vault, bus);
    }
}
