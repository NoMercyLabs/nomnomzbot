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
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The reconnect preview counts the channels that speak through the shared bot. A channel bound to the
/// shared account itself, or one whose own bot authorization was ended, still speaks through the shared bot.
/// Only an active authorization to a CUSTOM account takes a channel out of the count.
/// </summary>
public sealed class PlatformBotAdminAffectedChannelsTests
{
    [Fact]
    public async Task Preview_CountsOnlyChannelsWithoutAnActiveCustomBot()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount system = AddBot(db, AuthEnums.BotIdentityType.Shared, "nomz_bot");
        BotAccount own = AddBot(db, AuthEnums.BotIdentityType.Custom, "my_own_bot");
        Guid ownBotChannel = AddChannel(db, "own");
        Guid boundToSystem = AddChannel(db, "bound");
        AddChannel(db, "plain");
        db.ChannelBotAuthorizations.Add(NewAuthorization(ownBotChannel, own.Id));
        db.ChannelBotAuthorizations.Add(NewAuthorization(boundToSystem, system.Id));
        await db.SaveChangesAsync();

        Result<PlatformBotReconnectPreviewDto> result = await Sut(db)
            .PreviewReconnectAsync(Guid.NewGuid(), "rotate the system bot token");

        result.IsSuccess.Should().BeTrue();
        result.Value.AffectedChannelCount.Should().Be(2);
    }

    private static PlatformBotAdminService Sut(AuthDbContext db)
    {
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        iam.AuthorizePlatformAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Success(true));
        return new(
            db,
            Substitute.For<IAuthService>(),
            Substitute.For<IPlatformBotReadinessGate>(),
            iam
        );
    }

    private static Guid AddChannel(AuthDbContext db, string name)
    {
        Guid id = Guid.NewGuid();
        db.Channels.Add(
            new()
            {
                Id = id,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = name,
                TwitchChannelId = name,
                Name = name,
                NameNormalized = name,
                OwnerUserId = Guid.NewGuid(),
            }
        );
        return id;
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
        };
        db.BotAccounts.Add(bot);
        return bot;
    }

    private static ChannelBotAuthorization NewAuthorization(Guid channelId, Guid botAccountId) =>
        new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channelId,
            BotAccountId = botAccountId,
            AuthorizedAt = DateTime.UtcNow,
            IsActive = true,
        };
}
