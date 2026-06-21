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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves currency + earning-rule configuration (economy.md §3.1): the upserts are one-per-key (channel /
/// source), validate their inputs, earning rules are opt-in (default disabled), reads round-trip, and a delete
/// soft-removes a rule (a second delete is NOT_FOUND).
/// </summary>
public sealed class CurrencyConfigServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (CurrencyConfigService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new CurrencyConfigService(db, new FakeTimeProvider(Now)), db);
    }

    private static UpsertCurrencyConfigRequest Config(
        string name = "points",
        long starting = 100,
        long? max = null
    ) => new(name, null, null, true, starting, max, 0);

    [Fact]
    public async Task GetConfig_is_null_data_when_unconfigured()
    {
        (CurrencyConfigService sut, _) = Build();

        Result<CurrencyConfigDto?> result = await sut.GetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task UpsertConfig_creates_then_updates_the_single_row()
    {
        (CurrencyConfigService sut, AuthDbContext db) = Build();

        await sut.UpsertConfigAsync(Channel, Config(name: "points", starting: 50));
        await sut.UpsertConfigAsync(Channel, Config(name: "gold", starting: 75));

        (await db.CurrencyConfigs.CountAsync(c => c.BroadcasterId == Channel)).Should().Be(1);
        Result<CurrencyConfigDto?> read = await sut.GetConfigAsync(Channel);
        read.Value!.CurrencyName.Should().Be("gold");
        read.Value!.StartingBalance.Should().Be(75);
    }

    [Theory]
    [InlineData("", 100, null)] // empty name
    [InlineData("points", -1, null)] // negative starting balance
    [InlineData("points", 100, 50L)] // max < starting
    public async Task UpsertConfig_rejects_invalid_input(string name, long starting, long? max)
    {
        (CurrencyConfigService sut, _) = Build();

        Result<CurrencyConfigDto> result = await sut.UpsertConfigAsync(
            Channel,
            Config(name, starting, max)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task UpsertEarningRule_is_opt_in_and_keyed_by_source()
    {
        (CurrencyConfigService sut, _) = Build();

        Result<EarningRuleDto> created = await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("ChatMessage", false, 5, 60, 100, 1000, null, null)
        );
        await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("ChatMessage", true, 10, 60, 100, 1000, null, null)
        );

        created.Value.IsEnabled.Should().BeFalse(); // opt-in
        Result<IReadOnlyList<EarningRuleDto>> rules = await sut.ListEarningRulesAsync(Channel);
        rules.Value.Should().ContainSingle(); // one per (channel, source)
        rules.Value[0].IsEnabled.Should().BeTrue();
        rules.Value[0].Rate.Should().Be(10);
    }

    [Fact]
    public async Task UpsertEarningRule_rejects_an_unknown_source()
    {
        (CurrencyConfigService sut, _) = Build();

        Result<EarningRuleDto> result = await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("NotASource", true, 5, null, null, null, null, null)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task DeleteEarningRule_soft_removes_then_is_not_found()
    {
        (CurrencyConfigService sut, _) = Build();
        Result<EarningRuleDto> rule = await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("Cheer", true, 5, null, null, null, null, null)
        );

        (await sut.DeleteEarningRuleAsync(Channel, rule.Value.Id)).IsSuccess.Should().BeTrue();
        (await sut.ListEarningRulesAsync(Channel)).Value.Should().BeEmpty();
        (await sut.DeleteEarningRuleAsync(Channel, rule.Value.Id))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }
}
