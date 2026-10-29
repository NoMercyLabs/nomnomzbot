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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// The "an announcement in chat" prize: fulfilling a drawn winner posts one line in the channel's chat,
/// in the bot's voice, on the winner's own platform, naming the winner and the prize.
/// </summary>
public sealed class GiveawayAnnouncePrizeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f101");

    private static GiveawayFulfillment Build(AuthDbContext db, IInboundOriginChatSender chat) =>
        new(
            db,
            Substitute.For<ICurrencyAccountService>(),
            new ServiceCollection()
                .AddScoped<IPipelineEngine>(_ => Substitute.For<IPipelineEngine>())
                .BuildServiceProvider(),
            [],
            chat,
            Substitute.For<ITokenProtector>(),
            TimeProvider.System,
            NullLogger<GiveawayFulfillment>.Instance
        );

    private static (Giveaway Giveaway, GiveawayWinner Winner) SeedDraw(
        AuthDbContext db,
        string provider
    )
    {
        User viewer = new()
        {
            Id = Guid.CreateVersion7(),
            Username = "lucky",
            UsernameNormalized = "lucky",
            DisplayName = "Lucky",
            TwitchUserId = "tw-lucky",
        };
        Giveaway giveaway = new()
        {
            BroadcasterId = Channel,
            Title = "Signed poster",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Announce,
        };
        GiveawayWinner winner = new()
        {
            BroadcasterId = Channel,
            GiveawayId = giveaway.Id,
            ViewerUserId = viewer.Id,
            ViewerTwitchUserId = "tw-lucky",
            Provider = provider,
            ProviderUserId = "native-lucky",
            DrawnAt = DateTime.UtcNow,
        };
        db.Users.Add(viewer);
        db.Giveaways.Add(giveaway);
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();
        return (giveaway, winner);
    }

    [Fact]
    public async Task The_announce_prize_posts_one_bot_line_in_the_channel_naming_the_winner_and_the_prize()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(db, AuthEnums.Platform.Twitch);

        await Build(db, chat).FulfillAsync(giveaway, winner);

        IEnumerable<NSubstitute.Core.ICall> sent = chat.ReceivedCalls();
        sent.Should().ContainSingle();
        object?[] args = sent.Single().GetArguments();
        args[0].Should().Be(Channel);
        args[1].Should().Be(AuthEnums.Platform.Twitch);
        args[2].Should().BeOfType<string>().Which.Should().Contain("@Lucky");
        args[2].Should().BeOfType<string>().Which.Should().Contain("Signed poster");
    }

    [Fact]
    public async Task The_announcement_goes_out_on_the_platform_the_winner_is_on()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(db, AuthEnums.Platform.Kick);

        await Build(db, chat).FulfillAsync(giveaway, winner);

        await chat.Received(1)
            .SendMessageAsync(
                Channel,
                AuthEnums.Platform.Kick,
                Arg.Is<string>(m => m.Contains("Signed poster")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_chat_that_refuses_the_line_does_not_unwind_the_draw()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        chat.SendMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("no connection", "STUB"));
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(db, AuthEnums.Platform.Twitch);

        Func<Task> fulfill = () => Build(db, chat).FulfillAsync(giveaway, winner);

        await fulfill.Should().NotThrowAsync();
        await chat.Received(1)
            .SendMessageAsync(
                Channel,
                AuthEnums.Platform.Twitch,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
