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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior tests for the public read side of the widget gallery: the list/detail reads expose ONLY verified items
/// (the verified-only invariant that keeps unverified/in-review submissions off the anonymous surface), carry the
/// full DTO shape the install/clone UI needs, honor the framework filter, and order most-installed first.
/// </summary>
public sealed class WidgetGalleryServiceTests
{
    private static readonly PaginationParams FirstPage = new(1, 25);

    private static WidgetGalleryService NewService(WidgetTestDbContext db) =>
        new(db, new Identity.RecordingEventBus(), TimeProvider.System);

    private static async Task<Guid> SeedItemAsync(
        WidgetSqliteTestDatabase database,
        string name,
        string framework = "vanilla",
        string trustTier = "first_party",
        string reviewStatus = "verified",
        int installCount = 0,
        string? sourceCode = "GALLERY_SRC"
    )
    {
        Guid id = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new WidgetGalleryItem
            {
                Id = id,
                Name = name,
                Description = $"{name} description",
                Framework = framework,
                TrustTier = trustTier,
                SourceKind = "in_repo",
                NaturalKey = name.ToLowerInvariant(),
                SourceCode = sourceCode,
                ReviewStatus = reviewStatus,
                AvailableInSaaS = true,
                InstallCount = installCount,
                DefaultEventSubscriptions = ["follow", "cheer"],
                DefaultSettings = new Dictionary<string, object> { ["durationMs"] = 6000 },
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task List_returns_only_verified_items_most_installed_first_with_their_summary_shape()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedItemAsync(database, "Alerts", framework: "vanilla", installCount: 5);
        await SeedItemAsync(database, "Goals", framework: "vue", installCount: 12);
        // A non-verified submission must never leak through the public read.
        await SeedItemAsync(database, "Sketchy", reviewStatus: "submitted", installCount: 99);

        PagedList<GalleryItemSummary> page;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<PagedList<GalleryItemSummary>> result = await NewService(db)
                .ListAsync(new GalleryListRequest(), FirstPage);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            page = result.Value;
        }

        page.TotalCount.Should().Be(2); // the submitted item is excluded from the count too
        page.Items.Select(i => i.Name).Should().ContainInOrder("Goals", "Alerts"); // most-installed first
        page.Items.Should().NotContain(i => i.Name == "Sketchy");

        GalleryItemSummary top = page.Items[0];
        top.Name.Should().Be("Goals");
        top.Description.Should().Be("Goals description");
        top.Framework.Should().Be("vue");
        top.TrustTier.Should().Be("first_party");
        top.InstallCount.Should().Be(12);
        top.AvailableInSaaS.Should().BeTrue();
    }

    [Fact]
    public async Task List_honors_the_framework_filter()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedItemAsync(database, "Alerts", framework: "vanilla");
        await SeedItemAsync(database, "Goals", framework: "vue");

        await using WidgetTestDbContext db = database.NewContext();
        Result<PagedList<GalleryItemSummary>> result = await NewService(db)
            .ListAsync(new GalleryListRequest { Framework = "vue" }, FirstPage);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("Goals");
    }

    [Fact]
    public async Task List_honors_the_trust_tier_filter()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedItemAsync(database, "FirstParty", trustTier: "first_party");
        await SeedItemAsync(database, "Community", trustTier: "verified_community");

        await using WidgetTestDbContext db = database.NewContext();
        Result<PagedList<GalleryItemSummary>> result = await NewService(db)
            .ListAsync(new GalleryListRequest { TrustTier = "verified_community" }, FirstPage);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Items.Should().ContainSingle().Which.Name.Should().Be("Community");
    }

    [Fact]
    public async Task Get_returns_a_verified_items_detail_including_source()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid id = await SeedItemAsync(
            database,
            "Alerts",
            framework: "vanilla",
            installCount: 3,
            sourceCode: "SOURCE_TO_PREVIEW"
        );

        await using WidgetTestDbContext db = database.NewContext();
        Result<GalleryItemDetail> result = await NewService(db).GetAsync(id.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        GalleryItemDetail detail = result.Value;
        detail.Id.Should().Be(id);
        detail.Name.Should().Be("Alerts");
        detail.Description.Should().Be("Alerts description");
        detail.Framework.Should().Be("vanilla");
        detail.TrustTier.Should().Be("first_party");
        detail.InstallCount.Should().Be(3);
        detail.AvailableInSaaS.Should().BeTrue();
        detail.SourceKind.Should().Be("in_repo");
        detail.SourceCode.Should().Be("SOURCE_TO_PREVIEW"); // the preview source is carried on detail
        detail.DefaultEventSubscriptions.Should().Equal("follow", "cheer");
        detail.DefaultSettings.Should().ContainKey("durationMs");
    }

    [Fact]
    public async Task Get_a_non_verified_item_is_not_found()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid id = await SeedItemAsync(database, "Sketchy", reviewStatus: "submitted");

        await using WidgetTestDbContext db = database.NewContext();
        Result<GalleryItemDetail> result = await NewService(db).GetAsync(id.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Get_a_missing_item_is_not_found()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();

        await using WidgetTestDbContext db = database.NewContext();
        Result<GalleryItemDetail> result = await NewService(db)
            .GetAsync(Guid.CreateVersion7().ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
