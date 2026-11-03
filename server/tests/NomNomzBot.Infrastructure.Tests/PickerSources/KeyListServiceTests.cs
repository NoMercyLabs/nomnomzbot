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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.ViewerData.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using InfraViewerDataService = NomNomzBot.Infrastructure.ViewerData.ViewerDataService;

namespace NomNomzBot.Infrastructure.Tests.PickerSources;

/// <summary>
/// The list methods behind the counter-key and viewer-data-key pickers. Each test writes through the real service
/// and reads the list back, with a second channel's rows in the same database.
/// </summary>
public sealed class KeyListServiceTests
{
    private static readonly Guid Channel = Guid.NewGuid();
    private static readonly Guid OtherChannel = Guid.NewGuid();
    private static readonly PaginationParams FirstPage = new(Page: 1, PageSize: 25);

    /// <summary>The viewer-data service only writes for a viewer that exists, so each viewer is a saved user.</summary>
    internal static async Task<Guid> SeedViewerAsync(ViewerDataTestDbContext db)
    {
        Guid id = Guid.NewGuid();
        string name = $"viewer_{id:N}";
        db.Users.Add(
            new User
            {
                Id = id,
                TwitchUserId = id.ToString("N"),
                Username = name,
                UsernameNormalized = name,
                DisplayName = name,
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Counters_list_holds_this_channels_keys_with_their_values_in_key_order()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        NamedCounterService sut = new(db);
        await sut.SetAsync(Channel, "wins", 7);
        await sut.AdjustAsync(Channel, "deaths", 3);
        await sut.SetAsync(OtherChannel, "secret", 99);

        Result<PagedList<NamedCounterSummary>> list = await sut.ListAsync(Channel, null, FirstPage);

        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.Value.TotalCount);
        Assert.Equal(["deaths", "wins"], list.Value.Items.Select(c => c.Key));
        Assert.Equal([3L, 7L], list.Value.Items.Select(c => c.Value));
    }

    [Fact]
    public async Task Counters_list_searches_and_pages_by_key()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        NamedCounterService sut = new(db);
        await sut.SetAsync(Channel, "boss-kills", 1);
        await sut.SetAsync(Channel, "boss-deaths", 2);
        await sut.SetAsync(Channel, "wins", 3);

        Result<PagedList<NamedCounterSummary>> searched = await sut.ListAsync(
            Channel,
            "BOSS",
            FirstPage
        );
        Result<PagedList<NamedCounterSummary>> second = await sut.ListAsync(
            Channel,
            null,
            new PaginationParams(Page: 2, PageSize: 2)
        );

        Assert.Equal(["boss-deaths", "boss-kills"], searched.Value.Items.Select(c => c.Key));
        Assert.Equal(["wins"], second.Value.Items.Select(c => c.Key));
        Assert.Equal(3, second.Value.TotalCount);
    }

    [Fact]
    public async Task Viewer_data_keys_list_each_key_once_with_how_many_viewers_hold_it()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        InfraViewerDataService sut = new(db, TimeProvider.System);
        Guid first = await SeedViewerAsync(db);
        Guid second = await SeedViewerAsync(db);
        await sut.SetAsync(Channel, first, "deaths", "1");
        await sut.SetAsync(Channel, second, "deaths", "2");
        await sut.SetAsync(Channel, first, "favorite_game", "Celeste");
        await sut.SetAsync(OtherChannel, first, "private_key", "x");

        Result<PagedList<ViewerDataKeySummary>> list = await sut.ListKeysAsync(
            Channel,
            null,
            FirstPage
        );

        Assert.True(list.IsSuccess);
        Assert.Equal(2, list.Value.TotalCount);
        Assert.Equal(["deaths", "favorite_game"], list.Value.Items.Select(k => k.Key));
        Assert.Equal([2, 1], list.Value.Items.Select(k => k.ViewerCount));
    }

    [Fact]
    public async Task Viewer_data_keys_skip_a_cleared_key_and_honor_search_and_paging()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        InfraViewerDataService sut = new(db, TimeProvider.System);
        Guid viewer = await SeedViewerAsync(db);
        await sut.SetAsync(Channel, viewer, "quest_step", "1");
        await sut.SetAsync(Channel, viewer, "quest_done", "no");
        await sut.SetAsync(Channel, viewer, "gone", "x");
        await sut.DeleteAsync(Channel, viewer, "gone");

        Result<PagedList<ViewerDataKeySummary>> searched = await sut.ListKeysAsync(
            Channel,
            "QUEST",
            FirstPage
        );
        Result<PagedList<ViewerDataKeySummary>> all = await sut.ListKeysAsync(
            Channel,
            null,
            new PaginationParams(Page: 2, PageSize: 1)
        );

        Assert.Equal(["quest_done", "quest_step"], searched.Value.Items.Select(k => k.Key));
        Assert.Equal(["quest_step"], all.Value.Items.Select(k => k.Key));
        Assert.Equal(2, all.Value.TotalCount);
    }
}
