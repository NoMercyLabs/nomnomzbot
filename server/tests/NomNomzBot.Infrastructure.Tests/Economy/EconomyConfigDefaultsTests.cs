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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Every loyalty setting has one default (<see cref="CurrencyConfigDefaults"/>, <see cref="EarningRuleDefaults"/>,
/// <see cref="GameConfigDefaults"/>). A channel with no saved currency row reads those defaults from the API, a
/// new entity starts at them, and every settable field is saved and read back.
/// </summary>
public sealed class EconomyConfigDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static (CurrencyConfigService Sut, AuthDbContext Db) BuildCurrency()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new(db, new FakeTimeProvider(Now), new RecordingEventBus()), db);
    }

    [Fact]
    public async Task A_channel_with_no_saved_currency_row_reads_every_default_from_the_api()
    {
        (CurrencyConfigService sut, AuthDbContext db) = BuildCurrency();

        CurrencyConfigDto config = (await sut.GetConfigAsync(Channel)).Value;

        config.BroadcasterId.Should().Be(Channel);
        config.CurrencyName.Should().Be(CurrencyConfigDefaults.CurrencyName);
        config.CurrencyNamePlural.Should().Be(CurrencyConfigDefaults.CurrencyNamePlural);
        config.IconUrl.Should().Be(CurrencyConfigDefaults.IconUrl);
        config.IsEnabled.Should().Be(CurrencyConfigDefaults.IsEnabled);
        config.StartingBalance.Should().Be(CurrencyConfigDefaults.StartingBalance);
        config.MaxBalance.Should().Be(CurrencyConfigDefaults.MaxBalance);
        config.DecimalPlaces.Should().Be(CurrencyConfigDefaults.DecimalPlaces);
        db.CurrencyConfigs.Count().Should().Be(0); // reading never writes a row
    }

    [Fact]
    public void A_new_entity_starts_at_the_same_defaults_as_the_api()
    {
        CurrencyConfig currency = new();
        EarningRule rule = new();
        GameConfig game = new();

        currency.CurrencyName.Should().Be(CurrencyConfigDefaults.CurrencyName);
        currency.CurrencyNamePlural.Should().Be(CurrencyConfigDefaults.CurrencyNamePlural);
        currency.IsEnabled.Should().Be(CurrencyConfigDefaults.IsEnabled);
        currency.StartingBalance.Should().Be(CurrencyConfigDefaults.StartingBalance);
        currency.DecimalPlaces.Should().Be(CurrencyConfigDefaults.DecimalPlaces);
        rule.IsEnabled.Should().Be(EarningRuleDefaults.IsEnabled);
        rule.Rate.Should().Be(EarningRuleDefaults.Rate);
        rule.ConfigSchemaVersion.Should().Be(EarningRuleDefaults.ConfigSchemaVersion);
        game.IsEnabled.Should().Be(GameConfigDefaults.IsEnabled);
        game.Requires18Plus.Should().Be(GameConfigDefaults.Requires18Plus);
        game.CooldownSeconds.Should().Be(GameConfigDefaults.CooldownSeconds);
        game.Permission.Should().Be(GameConfigDefaults.Permission);
    }

    [Fact]
    public async Task Every_currency_field_is_saved_and_read_back_changed()
    {
        (CurrencyConfigService sut, _) = BuildCurrency();

        await sut.UpsertConfigAsync(
            Channel,
            new(
                "gold",
                "golds",
                "https://cdn.example/gold.png",
                !CurrencyConfigDefaults.IsEnabled,
                250,
                9000,
                2
            )
        );

        CurrencyConfigDto config = (await sut.GetConfigAsync(Channel)).Value;
        config.CurrencyName.Should().Be("gold");
        config.CurrencyNamePlural.Should().Be("golds");
        config.IconUrl.Should().Be("https://cdn.example/gold.png");
        config.IsEnabled.Should().Be(!CurrencyConfigDefaults.IsEnabled);
        config.StartingBalance.Should().Be(250);
        config.MaxBalance.Should().Be(9000);
        config.DecimalPlaces.Should().Be(2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(CurrencyConfigDefaults.MaxDecimalPlaces + 1)]
    public async Task Decimal_places_outside_the_allowed_range_are_rejected(int places)
    {
        (CurrencyConfigService sut, AuthDbContext db) = BuildCurrency();

        Result<CurrencyConfigDto> result = await sut.UpsertConfigAsync(
            Channel,
            new("points", null, null, true, 0, null, places)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.CurrencyConfigs.Count().Should().Be(0);
    }

    [Fact]
    public async Task Every_earning_rule_field_is_saved_and_read_back_changed()
    {
        (CurrencyConfigService sut, _) = BuildCurrency();

        await sut.UpsertEarningRuleAsync(
            Channel,
            new(
                "ChatMessage",
                !EarningRuleDefaults.IsEnabled,
                7,
                45,
                300,
                4000,
                2,
                new Dictionary<string, object?> { ["subMultiplier"] = 2L }
            )
        );

        EarningRuleDto rule = (await sut.ListEarningRulesAsync(Channel)).Value.Single();
        rule.Source.Should().Be("ChatMessage");
        rule.IsEnabled.Should().Be(!EarningRuleDefaults.IsEnabled);
        rule.Rate.Should().Be(7);
        rule.UnitWindowSeconds.Should().Be(45);
        rule.PerWindowCap.Should().Be(300);
        rule.PerStreamCap.Should().Be(4000);
        rule.MinRoleLevel.Should().Be(2);
        rule.ConfigSchemaVersion.Should().Be(EarningRuleDefaults.ConfigSchemaVersion);
        rule.BonusConfig.Should().ContainKey("subMultiplier");
    }

    [Fact]
    public async Task Every_game_field_is_saved_and_read_back_changed()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        GameService sut = new(
            db,
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IAgeConsentService>(),
            Substitute.For<IGameRandomizer>(),
            new RecordingEventBus(),
            new FakeTimeProvider(Now)
        );

        await sut.UpsertGameAsync(
            Channel,
            new(
                "dice",
                "Minigame",
                !GameConfigDefaults.IsEnabled,
                !GameConfigDefaults.Requires18Plus,
                5,
                500,
                3.5m,
                48m,
                1.9m,
                120,
                9,
                "Moderator",
                new Dictionary<string, object?> { ["sides"] = 6L }
            )
        );

        GameConfigDto game = (await sut.ListGamesAsync(Channel)).Value.Single(g =>
            g.GameType == "dice"
        );
        game.Category.Should().Be("Minigame");
        game.IsEnabled.Should().Be(!GameConfigDefaults.IsEnabled);
        game.Requires18Plus.Should().Be(!GameConfigDefaults.Requires18Plus);
        game.MinBet.Should().Be(5);
        game.MaxBet.Should().Be(500);
        game.HouseEdgePercent.Should().Be(3.5m);
        game.WinChancePercent.Should().Be(48m);
        game.PayoutMultiplier.Should().Be(1.9m);
        game.CooldownSeconds.Should().Be(120);
        game.MaxPlaysPerStream.Should().Be(9);
        game.Permission.Should().Be("Moderator");
        game.Config.Should().ContainKey("sides");
    }
}
