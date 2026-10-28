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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// The one-time repair for channels the old connect flow bound to the system bot account as their "own" bot.
/// It ends only the channel's binding. The system bot's account row and its token connection are never touched.
/// </summary>
public sealed class SharedBotBindingRepairSeederTests
{
    [Fact]
    public async Task ABindingToTheSystemAccount_IsEnded_AndTheSystemAccountIsLeftExactlyAsItWas()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid connectionId = Guid.NewGuid();
        BotAccount system = NewBot(AuthEnums.BotIdentityType.Shared, "nomz_bot", connectionId);
        Guid channelId = Guid.NewGuid();
        db.BotAccounts.Add(system);
        db.ChannelBotAuthorizations.Add(NewAuthorization(channelId, system.Id));
        db.SaveChanges();

        await Sut(db).SeedAsync();

        ChannelBotAuthorization binding = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .SingleAsync();
        binding.IsActive.Should().BeFalse();
        binding.DeletedAt.Should().NotBeNull();
        BotAccount account = await db.BotAccounts.IgnoreQueryFilters().SingleAsync();
        account.IdentityType.Should().Be(AuthEnums.BotIdentityType.Shared);
        account.IsActive.Should().BeTrue();
        account.DeletedAt.Should().BeNull();
        account.ConnectionId.Should().Be(connectionId);
    }

    [Fact]
    public async Task ABindingToAChannelsOwnCustomBot_IsKept()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount own = NewBot(AuthEnums.BotIdentityType.Custom, "my_own_bot", Guid.NewGuid());
        db.BotAccounts.Add(own);
        db.ChannelBotAuthorizations.Add(NewAuthorization(Guid.NewGuid(), own.Id));
        db.SaveChanges();

        await Sut(db).SeedAsync();

        ChannelBotAuthorization binding = await db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .SingleAsync();
        binding.IsActive.Should().BeTrue();
        binding.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunningTheRepairTwice_ChangesNothingTheSecondTime()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        BotAccount system = NewBot(AuthEnums.BotIdentityType.Shared, "nomz_bot", Guid.NewGuid());
        db.BotAccounts.Add(system);
        db.ChannelBotAuthorizations.Add(NewAuthorization(Guid.NewGuid(), system.Id));
        db.SaveChanges();
        SharedBotBindingRepairSeeder sut = Sut(db);

        await sut.SeedAsync();
        DateTime? firstEnd = (
            await db.ChannelBotAuthorizations.IgnoreQueryFilters().SingleAsync()
        ).DeletedAt;
        await sut.SeedAsync();

        (await db.ChannelBotAuthorizations.IgnoreQueryFilters().SingleAsync())
            .DeletedAt.Should()
            .Be(firstEnd);
    }

    private static SharedBotBindingRepairSeeder Sut(AuthDbContext db) =>
        new(db, TimeProvider.System, NullLogger<SharedBotBindingRepairSeeder>.Instance);

    private static BotAccount NewBot(string identityType, string username, Guid connectionId) =>
        new()
        {
            Id = Guid.NewGuid(),
            IdentityType = identityType,
            Platform = AuthEnums.Platform.Twitch,
            BotUserId = Guid.NewGuid().ToString("N")[..10],
            BotUsername = username,
            IsActive = true,
            ConnectionId = connectionId,
        };

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
