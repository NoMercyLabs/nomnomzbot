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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Giveaways.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves the giveaway chat reactions + the claim window (giveaways.md §5/D7): a message that IS the
/// open giveaway's keyword enters the chatter through the full entry path; a non-keyword message does
/// nothing; a drawn winner chatting inside the claim window flips to <c>claimed</c>; and the sweep
/// forfeits ONLY the winners whose window has actually elapsed — idempotently.
/// </summary>
public sealed class GiveawayChatFlowTests
{
    private static readonly Guid Tenant = Guid.Parse("0193c000-0000-7000-8000-0000000000a1");
    private static readonly Guid Viewer = Guid.Parse("0193c000-0000-7000-8000-0000000000a2");

    private static ChatMessageReceivedEvent Chat(string message) =>
        new()
        {
            BroadcasterId = Tenant,
            Provider = AuthEnums.Platform.Twitch,
            OccurredAt = DateTimeOffset.UtcNow,
            MessageId = Guid.NewGuid().ToString(),
            TwitchBroadcasterId = "tw-1",
            UserId = "111",
            UserDisplayName = "Chatter",
            UserLogin = "chatter",
            Message = message,
            Fragments = [new ChatMessageFragment { Type = "text", Text = message }],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static (
        GiveawayKeywordListener Listener,
        AuthDbContext Db,
        IGiveawayService Giveaways
    ) BuildListener()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IGiveawayService giveaways = Substitute.For<IGiveawayService>();
        giveaways
            .EnterAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new GiveawayEntryDto(Guid.NewGuid(), Guid.NewGuid(), Viewer, 1, DateTime.UtcNow)
                )
            );
        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        Viewer.ToString(),
                        "chatter",
                        "Chatter",
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow
                    )
                )
            );

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddScoped<IGiveawayService>(_ => giveaways)
            .BuildServiceProvider();
        GiveawayKeywordListener listener = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            users,
            NullLogger<GiveawayKeywordListener>.Instance
        );
        return (listener, db, giveaways);
    }

    private static Giveaway SeedGiveaway(AuthDbContext db, string status, int? claimWindow = null)
    {
        Giveaway giveaway = new()
        {
            BroadcasterId = Tenant,
            Title = "Drop",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Announce,
            Status = status,
            ClaimWindowMinutes = claimWindow,
        };
        db.Giveaways.Add(giveaway);
        db.SaveChanges();
        return giveaway;
    }

    [Fact]
    public async Task The_keyword_enters_the_chatter_and_other_messages_do_nothing()
    {
        (GiveawayKeywordListener listener, AuthDbContext db, IGiveawayService giveaways) =
            BuildListener();
        Giveaway giveaway = SeedGiveaway(db, GiveawayStatus.Open);

        await listener.HandleAsync(Chat("hello everyone"));
        await listener.HandleAsync(Chat("  !WIN ")); // trimmed + case-insensitive

        await giveaways
            .Received(1)
            .EnterAsync(Tenant, giveaway.Id, Viewer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_drawn_winner_chatting_inside_the_window_claims_their_win()
    {
        (GiveawayKeywordListener listener, AuthDbContext db, _) = BuildListener();
        Giveaway giveaway = SeedGiveaway(db, GiveawayStatus.Drawn, claimWindow: 10);
        GiveawayWinner winner = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Viewer,
            ViewerTwitchUserId = "111",
            DrawnAt = DateTime.UtcNow,
            Status = GiveawayWinnerStatus.Drawn,
        };
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();

        await listener.HandleAsync(Chat("I'm here!"));

        db.GiveawayWinners.Single().Status.Should().Be(GiveawayWinnerStatus.Claimed);
    }

    [Fact]
    public async Task The_sweep_forfeits_only_overdue_drawn_winners_idempotently()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        guard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IAsyncDisposable>());
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddScoped<IRunOnceGuard>(_ => guard)
            .BuildServiceProvider();
        GiveawayClaimSweepWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<GiveawayClaimSweepWorker>.Instance
        );

        Giveaway giveaway = SeedGiveaway(db, GiveawayStatus.Drawn, claimWindow: 10);
        GiveawayWinner overdue = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "111",
            DrawnAt = DateTime.UtcNow.AddMinutes(-30),
            Status = GiveawayWinnerStatus.Drawn,
        };
        GiveawayWinner fresh = new()
        {
            BroadcasterId = Tenant,
            GiveawayId = giveaway.Id,
            ViewerUserId = Guid.CreateVersion7(),
            ViewerTwitchUserId = "222",
            DrawnAt = DateTime.UtcNow,
            Status = GiveawayWinnerStatus.Drawn,
        };
        db.GiveawayWinners.AddRange(overdue, fresh);
        db.SaveChanges();

        await worker.SweepAsync(CancellationToken.None);
        await worker.SweepAsync(CancellationToken.None); // idempotent second pass

        db.GiveawayWinners.Single(w => w.Id == overdue.Id)
            .Status.Should()
            .Be(GiveawayWinnerStatus.Forfeited);
        db.GiveawayWinners.Single(w => w.Id == fresh.Id)
            .Status.Should()
            .Be(GiveawayWinnerStatus.Drawn, "the window has not elapsed for this winner");
    }
}
