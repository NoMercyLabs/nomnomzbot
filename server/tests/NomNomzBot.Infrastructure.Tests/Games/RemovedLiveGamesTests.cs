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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Games;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Games;

/// <summary>
/// Heist and Crash were removed on 2026-11-26 (Stoney: "these are not fun games to me. You may as well just
/// remove them from the system"). No game class, overlay or default config may bring them back, and a channel
/// that already holds their config rows no longer sees them on the Games page.
/// </summary>
public sealed class RemovedLiveGamesTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly string[] Removed = ["heist", "crash"];

    [Fact]
    public void No_live_game_class_is_registered_for_a_removed_game()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=removed_games_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        List<string> gameClasses =
        [
            .. services
                .Where(d => d.ServiceType == typeof(ILiveGame) && d.ImplementationType is not null)
                .Select(d => d.ImplementationType!.Name),
        ];

        gameClasses.Should().Contain(["DropGame", "RaffleGame"]);
        gameClasses.Should().NotContain(["HeistGame", "CrashGame"]);
    }

    [Fact]
    public void The_widget_catalogue_has_no_overlay_for_a_removed_game()
    {
        FirstPartyWidgetCatalogue
            .All.Select(w => w.Key)
            .Should()
            .NotContain(Removed, "a removed game has no overlay to install");
        FirstPartyWidgetCatalogue.DefaultAnchors.Keys.Should().NotContain(Removed);
    }

    [Fact]
    public async Task A_channel_seeded_before_the_removal_no_longer_lists_heist_or_crash()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        foreach (string type in new[] { "coinflip", "raffle", "heist", "crash" })
            db.GameConfigs.Add(
                new GameConfig
                {
                    BroadcasterId = Channel,
                    GameType = type,
                    Category = GameCategory.Gambling,
                    Permission = "Everyone",
                }
            );
        db.SaveChanges();
        GameService games = NewGameService(db);

        Result<IReadOnlyList<GameConfigDto>> listed = await games.ListGamesAsync(Channel);

        listed.IsSuccess.Should().BeTrue(listed.ErrorMessage);
        listed.Value.Select(g => g.GameType).Should().BeEquivalentTo(["coinflip", "raffle"]);
    }

    [Fact]
    public async Task A_new_channel_is_not_seeded_with_heist_or_crash()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        GameService games = NewGameService(db);

        Result<IReadOnlyList<GameConfigDto>> listed = await games.ListGamesAsync(Channel);

        listed.IsSuccess.Should().BeTrue(listed.ErrorMessage);
        db.GameConfigs.Where(g => g.BroadcasterId == Channel)
            .Select(g => g.GameType)
            .ToList()
            .Should()
            .BeEquivalentTo(["coinflip", "dice", "slots", "duel", "drop_game", "raffle"]);
    }

    private static GameService NewGameService(EventStoreTestDbContext db)
    {
        RecordingEventBus bus = new();
        FakeTimeProvider clock = new(new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            clock
        );
        return new(
            db,
            accounts,
            Substitute.For<IAgeConsentService>(),
            Substitute.For<IGameRandomizer>(),
            bus,
            clock
        );
    }
}
