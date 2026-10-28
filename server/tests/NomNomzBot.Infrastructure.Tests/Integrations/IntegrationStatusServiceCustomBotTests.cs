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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Integrations;

/// <summary>
/// The "Custom Bot" integration row reads the same truth as the bot status: it is connected only when the
/// channel has an active authorization to a CUSTOM bot account, and it names that account. The shared system
/// bot never counts, and the legacy Service table is not consulted.
/// </summary>
public sealed class IntegrationStatusServiceCustomBotTests
{
    private static readonly Guid Channel = Guid.Parse("0199f000-0000-7000-8000-0000000000c1");

    [Fact]
    public async Task CustomBot_IsConnectedAsTheChannelsOwnAccount_WhenAnActiveCustomAuthorizationExists()
    {
        AuthDbContext db = NewChannelContext();
        BotAccount own = AddBot(db, AuthEnums.BotIdentityType.Custom, "my_own_bot");
        AddAuthorization(db, own);
        await db.SaveChangesAsync();

        ChannelIntegrationDto row = await CustomBotRow(db);

        row.Connected.Should().BeTrue();
        row.ConnectedAs.Should().Be("my_own_bot");
    }

    [Fact]
    public async Task CustomBot_IsNotConnected_WhenTheChannelIsBoundToTheSharedSystemBot()
    {
        AuthDbContext db = NewChannelContext();
        BotAccount system = AddBot(db, AuthEnums.BotIdentityType.Shared, "nomz_bot");
        AddAuthorization(db, system);
        await db.SaveChangesAsync();

        ChannelIntegrationDto row = await CustomBotRow(db);

        row.Connected.Should().BeFalse();
        row.ConnectedAs.Should().BeNull();
    }

    [Fact]
    public async Task CustomBot_IsNotConnected_WhenTheCustomAuthorizationWasEnded()
    {
        AuthDbContext db = NewChannelContext();
        BotAccount own = AddBot(db, AuthEnums.BotIdentityType.Custom, "my_own_bot");
        ChannelBotAuthorization ended = AddAuthorization(db, own);
        ended.IsActive = false;
        await db.SaveChangesAsync();

        ChannelIntegrationDto row = await CustomBotRow(db);

        row.Connected.Should().BeFalse();
        row.ConnectedAs.Should().BeNull();
    }

    private static AuthDbContext NewChannelContext()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-c1",
                TwitchChannelId = "tw-c1",
                Name = "streamer",
                NameNormalized = "streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        return db;
    }

    private static BotAccount AddBot(AuthDbContext db, string identityType, string username)
    {
        BotAccount bot = new()
        {
            Id = Guid.NewGuid(),
            IdentityType = identityType,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = Guid.NewGuid().ToString("N")[..10],
            BotUsername = username,
            IsActive = true,
            ConnectionId = Guid.NewGuid(),
        };
        db.BotAccounts.Add(bot);
        return bot;
    }

    private static ChannelBotAuthorization AddAuthorization(AuthDbContext db, BotAccount bot)
    {
        ChannelBotAuthorization authorization = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = Channel,
            BotAccountId = bot.Id,
            AuthorizedAt = DateTime.UtcNow,
            IsActive = true,
        };
        db.ChannelBotAuthorizations.Add(authorization);
        return authorization;
    }

    private static async Task<ChannelIntegrationDto> CustomBotRow(AuthDbContext db)
    {
        IntegrationStatusService sut = new(db, Substitute.For<IMusicService>());
        Result<List<ChannelIntegrationDto>> result = await sut.GetStatusesAsync(Channel);
        result.IsSuccess.Should().BeTrue();
        return result.Value.Single(i => i.Id == "custom_bot");
    }
}
