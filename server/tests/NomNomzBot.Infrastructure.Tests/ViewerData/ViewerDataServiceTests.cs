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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.ViewerData;

namespace NomNomzBot.Infrastructure.Tests.ViewerData;

/// <summary>
/// Proves the per-viewer KV store's contract (per-viewer-data.md §3/§6): tenant+viewer-scoped upserts
/// with no duplicates, numeric adjust semantics (unset starts at delta, concurrent lost-update retries
/// so increments sum), bounded writes rejected — never truncated, soft-deleting removes from every read,
/// and honest typed failures for unknown viewers and bad keys.
/// </summary>
public sealed class ViewerDataServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-00000000c001");
    private static readonly Guid Viewer = Guid.Parse("0192b000-0000-7000-8000-00000000a001");
    private static readonly Guid OtherViewer = Guid.Parse("0192b000-0000-7000-8000-00000000a002");

    private static (ViewerDataService Sut, ViewerDataTestDbContext Db) Build()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        db.Users.Add(
            new User
            {
                Id = Viewer,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        db.Users.Add(
            new User
            {
                Id = OtherViewer,
                TwitchUserId = "222",
                Username = "bob",
                UsernameNormalized = "bob",
                DisplayName = "Bob",
            }
        );
        db.SaveChanges();
        return (new ViewerDataService(db, TimeProvider.System), db);
    }

    [Fact]
    public async Task Set_Upserts_SecondSetOverwritesWithoutDuplicate()
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();

        (await sut.SetAsync(Channel, Viewer, "favorite_game", "DOOM")).IsSuccess.Should().BeTrue();
        (await sut.SetAsync(Channel, Viewer, "favorite_game", "Quake")).IsSuccess.Should().BeTrue();

        List<ViewerDatum> rows = await db
            .ViewerData.Where(d => d.ViewerUserId == Viewer && d.Key == "favorite_game")
            .ToListAsync();
        rows.Should().ContainSingle().Which.Value.Should().Be("Quake");
        (await sut.GetAsync(Channel, Viewer, "favorite_game")).Value.Should().Be("Quake");
    }

    [Fact]
    public async Task Set_NormalizesTheKeyToALowercaseSlug()
    {
        (ViewerDataService sut, _) = Build();

        (await sut.SetAsync(Channel, Viewer, "  Deaths ", "3")).IsSuccess.Should().BeTrue();

        (await sut.GetAsync(Channel, Viewer, "deaths")).Value.Should().Be("3");
    }

    [Theory]
    [InlineData("no spaces allowed")]
    [InlineData("bang!")]
    [InlineData("")]
    public async Task Set_RejectsANonSlugKey_WithNoWrite(string badKey)
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();

        Result set = await sut.SetAsync(Channel, Viewer, badKey, "x");

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ViewerData.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Set_RejectsAnOverlongValue_WithNoWrite()
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();

        Result set = await sut.SetAsync(Channel, Viewer, "bio", new string('x', 501));

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ViewerData.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Set_RejectsTheKeyOverThePerViewerCap_ButExistingKeysStayWritable()
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();
        for (int i = 0; i < ViewerDataService.MaxKeysPerViewer; i++)
        {
            db.ViewerData.Add(
                new ViewerDatum
                {
                    BroadcasterId = Channel,
                    ViewerUserId = Viewer,
                    Key = $"k{i}",
                    Value = "v",
                }
            );
        }
        await db.SaveChangesAsync();

        Result overCap = await sut.SetAsync(Channel, Viewer, "one_more", "v");
        overCap.IsFailure.Should().BeTrue();
        overCap.ErrorCode.Should().Be("VALIDATION_FAILED");

        // An existing key updates fine, and a DIFFERENT viewer is not affected by this viewer's cap.
        (await sut.SetAsync(Channel, Viewer, "k0", "updated"))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.SetAsync(Channel, OtherViewer, "one_more", "v")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Set_ForAnUnknownViewer_FailsTyped()
    {
        (ViewerDataService sut, _) = Build();

        Result set = await sut.SetAsync(Channel, Guid.NewGuid(), "deaths", "1");

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Adjust_FromUnset_StartsAtDelta_AndSequentialAdjustsSum()
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();

        Result<long> first = await sut.AdjustAsync(Channel, Viewer, "deaths", 5);
        Result<long> second = await sut.AdjustAsync(Channel, Viewer, "deaths", 3);
        Result<long> third = await sut.AdjustAsync(Channel, Viewer, "deaths", -2);

        first.Value.Should().Be(5);
        second.Value.Should().Be(8);
        third.Value.Should().Be(6);
        ViewerDatum row = await db.ViewerData.SingleAsync(d => d.Key == "deaths");
        row.Value.Should().Be("6");
    }

    [Fact]
    public async Task Adjust_RetriesALostConcurrentUpdate_SoIncrementsSum()
    {
        string databaseName = Guid.NewGuid().ToString();
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New(databaseName);
        db.Users.Add(
            new User
            {
                Id = Viewer,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        await db.SaveChangesAsync();
        ViewerDataService sut = new(db, TimeProvider.System);
        (await sut.AdjustAsync(Channel, Viewer, "deaths", 5)).Value.Should().Be(5);

        // Stale the service's tracker: load the row into db's change tracker, then bump the STORE
        // through a second context — the service's next save loses the token race and must retry.
        ViewerDatum tracked = await db.ViewerData.SingleAsync(d => d.Key == "deaths");
        tracked.Value.Should().Be("5");
        ViewerDataTestDbContext rival = ViewerDataTestDbContext.New(databaseName);
        ViewerDatum rivalRow = await rival.ViewerData.SingleAsync(d => d.Key == "deaths");
        rivalRow.Value = "7";
        await rival.SaveChangesAsync();

        Result<long> adjusted = await sut.AdjustAsync(Channel, Viewer, "deaths", 1);

        // 7 (the concurrent write) + 1 — NOT 6 (the stale 5 + 1 that a lost update would produce).
        adjusted.IsSuccess.Should().BeTrue();
        adjusted.Value.Should().Be(8);
        ViewerDataTestDbContext verify = ViewerDataTestDbContext.New(databaseName);
        (await verify.ViewerData.SingleAsync(d => d.Key == "deaths")).Value.Should().Be("8");
    }

    [Fact]
    public async Task Adjust_OnANonNumericValue_FailsTyped_AndLeavesTheValue()
    {
        (ViewerDataService sut, _) = Build();
        await sut.SetAsync(Channel, Viewer, "favorite_game", "DOOM");

        Result<long> adjusted = await sut.AdjustAsync(Channel, Viewer, "favorite_game", 1);

        adjusted.IsFailure.Should().BeTrue();
        adjusted.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await sut.GetAsync(Channel, Viewer, "favorite_game")).Value.Should().Be("DOOM");
    }

    [Fact]
    public async Task Delete_SoftDeletes_AndEveryReadStopsSeeingTheKey()
    {
        (ViewerDataService sut, ViewerDataTestDbContext db) = Build();
        await sut.SetAsync(Channel, Viewer, "deaths", "3");

        (await sut.DeleteAsync(Channel, Viewer, "deaths")).IsSuccess.Should().BeTrue();

        (await sut.GetAsync(Channel, Viewer, "deaths")).Value.Should().BeNull();
        (await sut.ListForViewerAsync(Channel, Viewer)).Value.Should().BeEmpty();
        // Soft-deleted, not erased: the row survives with DeletedAt stamped.
        ViewerDatum tombstone = await db
            .ViewerData.IgnoreQueryFilters()
            .SingleAsync(d => d.Key == "deaths");
        tombstone.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_OfAnUnsetKey_FailsTyped()
    {
        (ViewerDataService sut, _) = Build();

        Result deleted = await sut.DeleteAsync(Channel, Viewer, "deaths");

        deleted.IsFailure.Should().BeTrue();
        deleted.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Set_AfterDelete_RecreatesTheKey()
    {
        (ViewerDataService sut, _) = Build();
        await sut.SetAsync(Channel, Viewer, "deaths", "3");
        await sut.DeleteAsync(Channel, Viewer, "deaths");

        (await sut.SetAsync(Channel, Viewer, "deaths", "0")).IsSuccess.Should().BeTrue();

        (await sut.GetAsync(Channel, Viewer, "deaths")).Value.Should().Be("0");
    }

    [Fact]
    public async Task Reads_AreTenantAndViewerScoped()
    {
        (ViewerDataService sut, _) = Build();
        Guid otherChannel = Guid.NewGuid();
        await sut.SetAsync(Channel, Viewer, "deaths", "3");
        await sut.SetAsync(otherChannel, Viewer, "deaths", "99");
        await sut.SetAsync(Channel, OtherViewer, "deaths", "50");

        (await sut.GetAsync(Channel, Viewer, "deaths")).Value.Should().Be("3");
        (await sut.GetAsync(otherChannel, Viewer, "deaths")).Value.Should().Be("99");
        (await sut.GetAsync(Channel, OtherViewer, "deaths")).Value.Should().Be("50");
    }

    [Fact]
    public async Task LoadKeys_ReturnsOnlyTheStoredOnesOfTheRequestedSet()
    {
        (ViewerDataService sut, _) = Build();
        await sut.SetAsync(Channel, Viewer, "deaths", "3");
        await sut.SetAsync(Channel, Viewer, "wins", "12");
        await sut.SetAsync(Channel, Viewer, "quest", "started");

        Result<IReadOnlyDictionary<string, string>> loaded = await sut.LoadKeysAsync(
            Channel,
            Viewer,
            ["deaths", "quest", "unset_key"]
        );

        loaded.Value.Should().HaveCount(2);
        loaded.Value["deaths"].Should().Be("3");
        loaded.Value["quest"].Should().Be("started");
        loaded.Value.Should().NotContainKey("unset_key");
    }
}
