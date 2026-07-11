// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.PipelineActions;
using NomNomzBot.Infrastructure.Tests.ViewerData;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the G.4 named counters finally do what their table always promised: <c>set_counter</c> /
/// <c>adjust_counter</c> persist per-channel counter state (unset starts at the delta, concurrent
/// lost-updates retry so increments sum) and publish the fresh value into <c>{count.&lt;key&gt;}</c>
/// for the rest of the run.
/// </summary>
public sealed class CounterActionsTests
{
    private static readonly Guid Channel = Guid.Parse("0192b200-0000-7000-8000-00000000c001");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "111",
            TriggeredByDisplayName = "Alice",
            MessageId = "m1",
            RawMessage = "!cmd",
            CancellationToken = default,
        };

    private static ActionDefinition Action(string type, params (string Key, object Value)[] p) =>
        new()
        {
            Type = type,
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    [Fact]
    public async Task SetCounter_PersistsTheAbsoluteValue_AndSeedsTheCountVariable()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        SetCounterAction sut = new(new NamedCounterService(db));
        PipelineExecutionContext ctx = Context();

        ActionResult result = await sut.ExecuteAsync(
            ctx,
            Action("set_counter", ("key", "Deaths"), ("value", "10"))
        );

        result.Succeeded.Should().BeTrue();
        NamedCounter row = await db.NamedCounters.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.Key.Should().Be("deaths");
        row.Value.Should().Be(10);
        ctx.Variables["count.deaths"].Should().Be("10");
    }

    [Fact]
    public async Task AdjustCounter_FromUnsetStartsAtDelta_AndSequentialAdjustsSum()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        AdjustCounterAction sut = new(new NamedCounterService(db));
        PipelineExecutionContext ctx = Context();
        ActionDefinition plusOne = Action("adjust_counter", ("key", "wins"));

        await sut.ExecuteAsync(ctx, plusOne); // delta defaults to 1
        ActionResult second = await sut.ExecuteAsync(
            ctx,
            Action("adjust_counter", ("key", "wins"), ("delta", "4"))
        );

        second.Succeeded.Should().BeTrue();
        second.Output.Should().Be("5");
        ctx.Variables["count.wins"].Should().Be("5");
        (await db.NamedCounters.SingleAsync(c => c.Key == "wins")).Value.Should().Be(5);
    }

    [Fact]
    public async Task AdjustCounter_RetriesALostConcurrentUpdate_SoIncrementsSum()
    {
        string databaseName = Guid.NewGuid().ToString();
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New(databaseName);
        NamedCounterService sut = new(db);
        (await sut.AdjustAsync(Channel, "raids", 5)).Value.Should().Be(5);

        // Stale the tracker, bump the store through a rival context — the next save must retry.
        NamedCounter tracked = await db.NamedCounters.SingleAsync(c => c.Key == "raids");
        tracked.Value.Should().Be(5);
        ViewerDataTestDbContext rival = ViewerDataTestDbContext.New(databaseName);
        NamedCounter rivalRow = await rival.NamedCounters.SingleAsync(c => c.Key == "raids");
        rivalRow.Value = 7;
        await rival.SaveChangesAsync();

        Result<long> adjusted = await sut.AdjustAsync(Channel, "raids", 1);

        adjusted.IsSuccess.Should().BeTrue();
        adjusted.Value.Should().Be(8); // 7 + 1, not the stale 5 + 1
    }

    [Fact]
    public async Task SetCounter_NonNumericValue_FailsWithoutWriting()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        SetCounterAction sut = new(new NamedCounterService(db));

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action("set_counter", ("key", "deaths"), ("value", "many"))
        );

        result.Succeeded.Should().BeFalse();
        (await db.NamedCounters.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LoadKeys_ReturnsOnlyStoredCounters_TenantScoped()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        NamedCounterService sut = new(db);
        await sut.SetAsync(Channel, "wins", 7);
        await sut.SetAsync(Guid.NewGuid(), "losses", 3); // another channel's counter

        Result<IReadOnlyDictionary<string, long>> loaded = await sut.LoadKeysAsync(
            Channel,
            ["wins", "losses"]
        );

        loaded.Value.Should().HaveCount(1);
        loaded.Value["wins"].Should().Be(7);
    }
}
