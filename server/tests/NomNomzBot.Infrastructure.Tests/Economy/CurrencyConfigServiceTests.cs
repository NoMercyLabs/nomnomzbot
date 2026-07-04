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
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves currency + earning-rule configuration (economy.md §3.1): the upserts are one-per-key (channel /
/// source), validate their inputs, earning rules are opt-in (default disabled), reads round-trip, a delete
/// soft-removes a rule (a second delete is NOT_FOUND), and every successful write publishes the E5 dashboard
/// live-sync event (a rejected write publishes nothing).
/// </summary>
public sealed class CurrencyConfigServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000b1");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    private static (CurrencyConfigService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        return (new CurrencyConfigService(db, new FakeTimeProvider(Now), bus), db, bus);
    }

    private static UpsertCurrencyConfigRequest Config(
        string name = "points",
        long starting = 100,
        long? max = null
    ) => new(name, null, null, true, starting, max, 0);

    [Fact]
    public async Task GetConfig_is_null_data_when_unconfigured()
    {
        (CurrencyConfigService sut, _, _) = Build();

        Result<CurrencyConfigDto?> result = await sut.GetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task UpsertConfig_creates_then_updates_the_single_row_and_publishes_each_time()
    {
        (CurrencyConfigService sut, AuthDbContext db, RecordingEventBus bus) = Build();

        await sut.UpsertConfigAsync(Channel, Config(name: "points", starting: 50));
        await sut.UpsertConfigAsync(Channel, Config(name: "gold", starting: 75));

        (await db.CurrencyConfigs.CountAsync(c => c.BroadcasterId == Channel)).Should().Be(1);
        Result<CurrencyConfigDto?> read = await sut.GetConfigAsync(Channel);
        read.Value!.CurrencyName.Should().Be("gold");
        read.Value!.StartingBalance.Should().Be(75);
        List<ChannelConfigChangedEvent> published =
        [
            .. bus.Published.OfType<ChannelConfigChangedEvent>(),
        ];
        published.Should().HaveCount(2);
        published[0].Action.Should().Be("created");
        published[1].Action.Should().Be("updated");
        published
            .Should()
            .OnlyContain(e => e.Domain == "economy-config" && e.BroadcasterId == Channel);
    }

    [Theory]
    [InlineData("", 100, null)] // empty name
    [InlineData("points", -1, null)] // negative starting balance
    [InlineData("points", 100, 50L)] // max < starting
    public async Task UpsertConfig_rejects_invalid_input(string name, long starting, long? max)
    {
        (CurrencyConfigService sut, _, RecordingEventBus bus) = Build();

        Result<CurrencyConfigDto> result = await sut.UpsertConfigAsync(
            Channel,
            Config(name, starting, max)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task UpsertEarningRule_is_opt_in_and_keyed_by_source()
    {
        (CurrencyConfigService sut, _, RecordingEventBus bus) = Build();

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
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .SatisfyRespectively(
                e => e.Action.Should().Be("created"),
                e => e.Action.Should().Be("updated")
            );
    }

    [Fact]
    public async Task UpsertEarningRule_rejects_an_unknown_source()
    {
        (CurrencyConfigService sut, _, RecordingEventBus bus) = Build();

        Result<EarningRuleDto> result = await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("NotASource", true, 5, null, null, null, null, null)
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEarningRule_soft_removes_then_is_not_found()
    {
        (CurrencyConfigService sut, _, RecordingEventBus bus) = Build();
        Result<EarningRuleDto> rule = await sut.UpsertEarningRuleAsync(
            Channel,
            new UpsertEarningRuleRequest("Cheer", true, 5, null, null, null, null, null)
        );
        bus.Published.Clear();

        (await sut.DeleteEarningRuleAsync(Channel, rule.Value.Id)).IsSuccess.Should().BeTrue();
        (await sut.ListEarningRulesAsync(Channel)).Value.Should().BeEmpty();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e => e.Domain == "earning-rules" && e.Action == "deleted");

        bus.Published.Clear();
        (await sut.DeleteEarningRuleAsync(Channel, rule.Value.Id))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
        bus.Published.Should().BeEmpty();
    }
}
