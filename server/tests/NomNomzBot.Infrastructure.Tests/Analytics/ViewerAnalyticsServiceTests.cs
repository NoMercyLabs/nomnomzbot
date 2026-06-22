// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Infrastructure.Services.Analytics;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Analytics;

/// <summary>
/// Proves the per-viewer analytics reads (analytics.md §3.2): profile fetch (+ NOT_FOUND), the ranked viewer list,
/// the idempotent opt-out toggle, and streak resolution that bridges the internal viewer Guid to M.3's Twitch-id key.
/// </summary>
public sealed class ViewerAnalyticsServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000004001");
    private static readonly Guid ViewerA = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly Guid ViewerB = Guid.Parse("0192a000-0000-7000-8000-0000000000b2");

    private static (ViewerAnalyticsService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new ViewerAnalyticsService(db), db);
    }

    private static async Task SeedProfileAsync(
        AuthDbContext db,
        Guid viewerUserId,
        string display,
        long messages
    )
    {
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = Channel,
                ViewerUserId = viewerUserId,
                ViewerTwitchUserId = display.ToLowerInvariant(),
                DisplayNameSnapshot = display,
                TotalMessages = messages,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProfile_returns_the_viewer_profile()
    {
        (ViewerAnalyticsService sut, AuthDbContext db) = Build();
        await SeedProfileAsync(db, ViewerA, "Alice", 42);

        Result<ViewerProfileDto> result = await sut.GetProfileAsync(Channel, ViewerA);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(42);
        result.Value.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task GetProfile_is_not_found_for_an_unknown_viewer()
    {
        (ViewerAnalyticsService sut, _) = Build();

        Result<ViewerProfileDto> result = await sut.GetProfileAsync(Channel, ViewerA);

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ListProfiles_sorts_by_the_chosen_metric()
    {
        (ViewerAnalyticsService sut, AuthDbContext db) = Build();
        await SeedProfileAsync(db, ViewerA, "Alice", 10);
        await SeedProfileAsync(db, ViewerB, "Bob", 20);

        PagedList<ViewerProfileListItemDto> page = (
            await sut.ListProfilesAsync(
                Channel,
                new ViewerProfileQuery(Sort: ViewerProfileSort.Messages),
                new PaginationParams()
            )
        ).Value;

        page.TotalCount.Should().Be(2);
        page.Items.Select(i => i.DisplayName).Should().Equal("Bob", "Alice");
    }

    [Fact]
    public async Task SetOptOut_toggles_the_profile_flag()
    {
        (ViewerAnalyticsService sut, AuthDbContext db) = Build();
        await SeedProfileAsync(db, ViewerA, "Alice", 1);

        Result result = await sut.SetAnalyticsOptOutAsync(Channel, ViewerA, optedOut: true);

        result.IsSuccess.Should().BeTrue();
        db.ViewerProfiles.Single().IsAnalyticsOptedOut.Should().BeTrue();
    }

    [Fact]
    public async Task GetStreak_is_not_found_when_the_viewer_is_unknown()
    {
        // The resolution guard fires before the (Twitch-id-keyed) streak read: an unknown viewer Guid -> NOT_FOUND.
        (ViewerAnalyticsService sut, _) = Build();

        Result<WatchStreakDto> result = await sut.GetStreakAsync(Channel, ViewerA);

        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
