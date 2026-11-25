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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The overlay theme color lives in the per-channel configuration key <c>overlay.themeColor</c>: a valid hex
/// value is stored, an empty one clears the row (the reset), and anything else is refused with nothing written.
/// </summary>
public sealed class OverlayThemeColorServiceTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "tok",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<List<Configuration>> RowsAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        return await db.Configurations.ToListAsync();
    }

    [Fact]
    public async Task A_channel_with_nothing_stored_reads_an_empty_color_and_nothing_is_written()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(database);
        await using WidgetTestDbContext db = database.NewContext();

        Result<OverlayThemeColor> result = await new OverlayThemeColorService(db).GetAsync(
            ChannelId
        );

        result.Value.Color.Should().BeEmpty();
        (await RowsAsync(database)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_valid_color_is_stored_under_the_key_and_a_second_write_replaces_it()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(database);

        await using (WidgetTestDbContext db = database.NewContext())
            await new OverlayThemeColorService(db).SetAsync(ChannelId, new("#12AB34"));
        await using (WidgetTestDbContext db = database.NewContext())
            await new OverlayThemeColorService(db).SetAsync(ChannelId, new("#ff0000"));

        Configuration row = (await RowsAsync(database)).Single();
        row.BroadcasterId.Should().Be(ChannelId);
        row.Key.Should().Be("overlay.themeColor");
        row.Value.Should().Be("#ff0000");
    }

    [Fact]
    public async Task An_empty_color_removes_the_row_so_the_theme_own_color_applies_again()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(database);
        await using (WidgetTestDbContext db = database.NewContext())
            await new OverlayThemeColorService(db).SetAsync(ChannelId, new("#12AB34"));

        await using WidgetTestDbContext reset = database.NewContext();
        Result<OverlayThemeColor> result = await new OverlayThemeColorService(reset).SetAsync(
            ChannelId,
            new("")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Color.Should().BeEmpty();
        (await RowsAsync(database)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#12AB34FF")]
    [InlineData("12AB34")]
    [InlineData("#GG0000")]
    public async Task Anything_but_a_six_digit_hex_color_is_refused_and_nothing_is_written(
        string color
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedChannelAsync(database);
        await using WidgetTestDbContext db = database.NewContext();

        Result<OverlayThemeColor> result = await new OverlayThemeColorService(db).SetAsync(
            ChannelId,
            new(color)
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("overlay_theme_color_invalid");
        (await RowsAsync(database)).Should().BeEmpty();
    }
}
