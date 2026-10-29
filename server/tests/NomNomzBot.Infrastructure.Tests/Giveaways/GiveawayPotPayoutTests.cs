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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves the prize pot (giveaways.md §4) is paid out in full through the real ledger: it is split by
/// the winners actually drawn, not by the configured Number of winners, and the remainder goes to the
/// first winner.
/// </summary>
public sealed class GiveawayPotPayoutTests
{
    private static readonly Guid Tenant = Guid.Parse("0193c000-0000-7000-8000-0000000000a1");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    private sealed record Harness(
        GiveawayFulfillment Fulfillment,
        CurrencyAccountService Accounts,
        AuthDbContext Db
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.CurrencyConfigs.Add(
            new()
            {
                BroadcasterId = Tenant,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 0,
            }
        );
        db.SaveChanges();

        CurrencyAccountService accounts = new(
            db,
            new CountingTenantSequenceAllocator(),
            new PassThroughUnitOfWork(),
            new RecordingEventBus(),
            Clock
        );
        GiveawayFulfillment fulfillment = new(
            db,
            accounts,
            new ServiceCollection()
                .AddScoped<IPipelineEngine>(_ => Substitute.For<IPipelineEngine>())
                .BuildServiceProvider(),
            [],
            Substitute.For<ITokenProtector>(),
            Clock,
            NullLogger<GiveawayFulfillment>.Instance
        );
        return new(fulfillment, accounts, db);
    }

    // The draw's own order: winner rows are saved first, then each one is fulfilled in turn.
    private static async Task<List<GiveawayWinner>> DrawAndFulfillAsync(
        Harness harness,
        long entryCost,
        int paidEntries,
        int drawnWinners,
        int configuredWinners
    )
    {
        Giveaway giveaway = new()
        {
            BroadcasterId = Tenant,
            Title = "Pot",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Currency,
            PrizeFromPot = true,
            EntryCost = entryCost,
            WinnerCount = configuredWinners,
        };
        harness.Db.Giveaways.Add(giveaway);
        for (int i = 0; i < paidEntries; i++)
            harness.Db.GiveawayEntries.Add(
                new()
                {
                    BroadcasterId = Tenant,
                    GiveawayId = giveaway.Id,
                    ViewerUserId = Guid.CreateVersion7(),
                    ViewerTwitchUserId = $"entrant-{i}",
                    EntryCostLedgerEntryId = i + 1,
                    EnteredAt = DateTime.UtcNow,
                }
            );

        List<GiveawayWinner> winners = [];
        for (int i = 0; i < drawnWinners; i++)
            winners.Add(
                new()
                {
                    BroadcasterId = Tenant,
                    GiveawayId = giveaway.Id,
                    ViewerUserId = Guid.CreateVersion7(),
                    ViewerTwitchUserId = $"winner-{i}",
                    DrawnAt = DateTime.UtcNow,
                }
            );
        harness.Db.GiveawayWinners.AddRange(winners);
        harness.Db.SaveChanges();

        foreach (GiveawayWinner winner in winners)
            await harness.Fulfillment.FulfillAsync(giveaway, winner);
        harness.Db.SaveChanges();
        return winners;
    }

    [Fact]
    public async Task A_pot_with_fewer_entrants_than_the_winner_count_is_paid_out_in_full()
    {
        Harness harness = Build();

        // Number of winners is 3 but only 2 entrants exist, so only 2 are drawn; the pot is 100.
        List<GiveawayWinner> winners = await DrawAndFulfillAsync(
            harness,
            entryCost: 50,
            paidEntries: 2,
            drawnWinners: 2,
            configuredWinners: 3
        );

        long first = (
            await harness.Accounts.GetBalanceAsync(Tenant, winners[0].ViewerUserId)
        ).Value;
        long second = (
            await harness.Accounts.GetBalanceAsync(Tenant, winners[1].ViewerUserId)
        ).Value;
        first.Should().Be(50);
        second.Should().Be(50);
        (first + second).Should().Be(100, "the whole pot is paid, none of it is left unpaid");
        winners.Should().OnlyContain(w => w.FulfillmentLedgerEntryId != null);
    }

    [Fact]
    public async Task The_remainder_of_an_uneven_pot_goes_to_the_first_winner_only()
    {
        Harness harness = Build();

        // One paid entry of 101 gives a pot of 101 for 2 drawn winners: 50 each and 1 left over.
        List<GiveawayWinner> winners = await DrawAndFulfillAsync(
            harness,
            entryCost: 101,
            paidEntries: 1,
            drawnWinners: 2,
            configuredWinners: 3
        );

        long first = (
            await harness.Accounts.GetBalanceAsync(Tenant, winners[0].ViewerUserId)
        ).Value;
        long second = (
            await harness.Accounts.GetBalanceAsync(Tenant, winners[1].ViewerUserId)
        ).Value;
        first.Should().Be(51, "the first winner takes the remainder");
        second.Should().Be(50);
        (first + second).Should().Be(101);
    }
}
