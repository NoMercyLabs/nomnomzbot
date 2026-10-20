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
/// The dashboard's "The bot is not in chat" row reads this status. Most channels use the shared system bot and
/// have no per-channel bot authorization, so a status that only looked at the white-label authorization told
/// every one of them the bot was out of chat (found on ifillz, 2026-10-18). The status is the truth about the
/// bot that speaks in the channel: the channel's own bot when it has one, otherwise the shared bot, and only
/// while the channel is actually served.
/// </summary>
public sealed class AuthServiceChannelBotStatusTests
{
    private const string SharedBotUserId = "1335549269";

    [Fact]
    public async Task AServedChannelWithoutItsOwnBot_ReportsTheSharedBotInChat()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        Guid connectionId = SeedSharedBot(db);
        VaultHasToken(vault, connectionId);
        Channel channel = SeedChannel(db);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Connected.Should().BeTrue();
        result.Value.Login.Should().Be("nomz_bot");
        result.Value.DisplayName.Should().Be("nomz_bot");
    }

    [Fact]
    public async Task TheStatusCarriesTheBotsOwnPictureFromItsUserRow()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        Guid connectionId = SeedSharedBot(db);
        VaultHasToken(vault, connectionId);
        db.Users.Add(
            new User
            {
                TwitchUserId = SharedBotUserId,
                Username = "nomz_bot",
                UsernameNormalized = "nomz_bot",
                DisplayName = "nomz_bot",
                ProfileImageUrl = "https://static-cdn.example/nomz_bot.png",
            }
        );
        db.SaveChanges();
        Channel channel = SeedChannel(db);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.ProfileImageUrl.Should().Be("https://static-cdn.example/nomz_bot.png");
    }

    [Fact]
    public async Task WithNoSharedBotRegistered_TheBotIsNotInChat()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault _) = Build();
        Channel channel = SeedChannel(db);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.Connected.Should().BeFalse();
        result.Value.Login.Should().BeNull();
    }

    [Fact]
    public async Task ASharedBotWhoseTokenCannotBeRead_IsNotInChat()
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        SeedSharedBot(db);
        vault
            .GetAccessTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<DecryptedTokenDto>("NO_TOKEN", "revoked"));
        Channel channel = SeedChannel(db);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.Connected.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true, AuthEnums.ChannelStatus.Active)]
    [InlineData(true, false, AuthEnums.ChannelStatus.Active)]
    [InlineData(true, true, AuthEnums.ChannelStatus.Suspended)]
    public async Task AChannelTheBotDoesNotServe_IsNotInChat(
        bool enabled,
        bool onboarded,
        string status
    )
    {
        (AuthService service, AuthDbContext db, IIntegrationTokenVault vault) = Build();
        Guid connectionId = SeedSharedBot(db);
        VaultHasToken(vault, connectionId);
        Channel channel = SeedChannel(db, enabled, onboarded, status);

        Result<BotStatusDto> result = await service.GetChannelBotStatusAsync(channel.Id);

        result.Value.Connected.Should().BeFalse();
    }

    private static void VaultHasToken(IIntegrationTokenVault vault, Guid connectionId) =>
        vault
            .GetAccessTokenAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto("token", "Bearer", null, false)));

    private static Guid SeedSharedBot(AuthDbContext db)
    {
        Guid connectionId = Guid.NewGuid();
        db.BotAccounts.Add(
            new BotAccount
            {
                Id = Guid.NewGuid(),
                IdentityType = AuthEnums.BotIdentityType.Shared,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = SharedBotUserId,
                BotUsername = "nomz_bot",
                IsActive = true,
                ConnectionId = connectionId,
            }
        );
        db.SaveChanges();
        return connectionId;
    }

    private static Channel SeedChannel(
        AuthDbContext db,
        bool enabled = true,
        bool onboarded = true,
        string status = AuthEnums.ChannelStatus.Active
    )
    {
        DateTime now = DateTime.UtcNow;
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = Guid.NewGuid(),
            Name = "ifillz",
            NameNormalized = "ifillz",
            Enabled = enabled,
            IsOnboarded = onboarded,
            Status = status,
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
