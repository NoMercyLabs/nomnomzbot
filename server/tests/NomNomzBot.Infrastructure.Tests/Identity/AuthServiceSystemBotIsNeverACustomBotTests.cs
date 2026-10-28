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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The system bot (the shared platform account, nomz_bot) can never be a channel's own bot. Live finding,
/// 2026-10-18: a channel connected "its own bot" with the system account, the connect flow found the shared
/// BotAccount by Twitch user id, bound the channel to it and re-pointed the shared account at the channel's
/// connection, and the dashboard then showed the system bot as the channel's white-label bot.
/// </summary>
public sealed class AuthServiceSystemBotIsNeverACustomBotTests
{
    private const string SystemBotUserId = "1335549269";

    [Fact]
    public async Task ConnectingTheSystemAccountAsAChannelsOwnBot_IsRefusedAndChangesNothing()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        BotAccount system = SeedSystemBot(db);
        Guid originalConnection = system.ConnectionId!.Value;
        Guid channelId = Guid.NewGuid();

        Result<BotStatusDto> result = await service.EstablishChannelBotAsync(
            channelId,
            new(SystemBotUserId, "nomz_bot", "nomz_bot", null, ""),
            new("access", "refresh", DateTime.UtcNow.AddHours(4), ["user:write:chat"])
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SYSTEM_BOT_NOT_ALLOWED");
        (await db.ChannelBotAuthorizations.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
        BotAccount after = await db.BotAccounts.IgnoreQueryFilters().SingleAsync();
        after.IdentityType.Should().Be(AuthEnums.BotIdentityType.Shared);
        after.ConnectionId.Should().Be(originalConnection);
        await vault.DidNotReceiveWithAnyArgs().UpsertConnectionAsync(default!, default);
    }

    [Fact]
    public async Task AChannelBoundToTheSystemAccount_ReadsAsUsingTheSharedBot()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        BotAccount system = SeedSystemBot(db);
        vault
            .GetAccessTokenAsync(system.ConnectionId!.Value, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto("token", "Bearer", null, false)));
        Channel channel = SeedChannel(db);
        SeedAuthorization(db, channel.Id, system.Id);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.UsesSharedBot.Should().BeTrue();
        result.Value.Connected.Should().BeTrue();
        result.Value.Login.Should().Be("nomz_bot");
    }

    [Fact]
    public async Task AChannelWithItsOwnCustomBot_DoesNotReadAsUsingTheSharedBot()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        BotAccount own = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = AuthEnums.BotIdentityType.Custom,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = "42",
            BotUsername = "my_own_bot",
            IsActive = true,
            ConnectionId = Guid.NewGuid(),
        };
        db.BotAccounts.Add(own);
        db.SaveChanges();
        vault
            .GetAccessTokenAsync(own.ConnectionId!.Value, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto("token", "Bearer", null, false)));
        Channel channel = SeedChannel(db);
        SeedAuthorization(db, channel.Id, own.Id);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.UsesSharedBot.Should().BeFalse();
        result.Value.Login.Should().Be("my_own_bot");
    }

    [Fact]
    public async Task AChannelWithNoBotOfItsOwn_ReadsAsUsingTheSharedBot()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        BotAccount system = SeedSystemBot(db);
        vault
            .GetAccessTokenAsync(system.ConnectionId!.Value, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto("token", "Bearer", null, false)));
        Channel channel = SeedChannel(db);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.UsesSharedBot.Should().BeTrue();
    }

    private static BotAccount SeedSystemBot(AuthDbContext db)
    {
        BotAccount bot = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = AuthEnums.BotIdentityType.Shared,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = SystemBotUserId,
            BotUsername = "nomz_bot",
            IsActive = true,
            ConnectionId = Guid.NewGuid(),
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

    private static Channel SeedChannel(AuthDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Guid.NewGuid(),
            Name = "stoney_eagle",
            NameNormalized = "stoney_eagle",
            Enabled = true,
            IsOnboarded = true,
            Status = AuthEnums.ChannelStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Channels.Add(channel);
        db.SaveChanges();
        return channel;
    }

    private static (AuthService Service, AuthDbContext Db, IIntegrationTokenVault Vault) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Twitch:ClientId"] = "cid" })
            .Build();
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();

        AuthService service = new(
            db,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchDeviceCodeService>(),
            vault,
            Substitute.For<ISessionService>(),
            Substitute.For<ISessionRevocationService>(),
            new RecordingEventBus(),
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

        return (service, db, vault);
    }
}
