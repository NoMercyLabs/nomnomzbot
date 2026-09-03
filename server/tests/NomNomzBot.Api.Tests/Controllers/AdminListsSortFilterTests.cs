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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-OWN08: both admin lists were hard-ordered newest-first with no filter, so an operator looking for a
/// specific channel or the platform staff among thousands of users had only free-text search and luck.
///
/// <para>These run REAL queries against a seeded database and assert the returned ORDER and the returned SET —
/// a sort that returns the right rows in the wrong order, or a filter that merely deprioritizes rather than
/// excludes, would pass a mock-based test and fail a person.</para>
///
/// <para>The unknown-sort case matters as much as the known ones: the ordering is chosen from a closed set,
/// and a stale bookmark carrying a retired sort key must fall back to the default rather than 400 or, worse,
/// interpolate the caller's string into the query.</para>
/// </summary>
public sealed class AdminListsSortFilterTests
{
    private static readonly DateTime Old = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (AdminController Controller, AdminListsSearchTestDbContext Db) Build()
    {
        AdminListsSearchTestDbContext db = AdminListsSearchTestDbContext.New();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddHealthChecks();
        ServiceProvider provider = services.BuildServiceProvider();

        AdminService adminService = new(
            db,
            TimeProvider.System,
            provider.GetRequiredService<HealthCheckService>(),
            Substitute.For<IPlatformBotReadinessGate>()
        );

        return (new(adminService, db, Substitute.For<IDekRotationService>(), Substitute.For<IProviderCredentialService>()), db);
    }

    private static void SeedChannel(
        AdminListsSearchTestDbContext db,
        string login,
        DateTime createdAt,
        bool isLive = false
    )
    {
        Guid ownerId = Guid.NewGuid();
        db.Users.Add(
            new()
            {
                Id = ownerId,
                Username = login,
                UsernameNormalized = login,
                DisplayName = login,
            }
        );
        db.Channels.Add(
            new()
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerId,
                Name = login,
                NameNormalized = login,
                CreatedAt = createdAt,
                IsLive = isLive,
            }
        );
    }

    private static void SeedUser(
        AdminListsSearchTestDbContext db,
        string login,
        DateTime createdAt,
        bool isPlatformPrincipal = true
    )
    {
        db.Users.Add(
            new()
            {
                Id = Guid.NewGuid(),
                Username = login,
                UsernameNormalized = login,
                DisplayName = login,
                CreatedAt = createdAt,
                // A non-principal still has to pass the "real bot user" filter to reach the role filter at
                // all, and owning a channel is the other way in.
                IsPlatformPrincipal = isPlatformPrincipal,
            }
        );
        if (!isPlatformPrincipal)
        {
            Guid ownerId = db.Users.Local.Last().Id;
            db.Channels.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = ownerId,
                    Name = login + "-ch",
                    NameNormalized = login + "-ch",
                    CreatedAt = createdAt,
                }
            );
        }
    }

    private static PaginatedResponse<T> Body<T>(IActionResult result) =>
        result
            .Should()
            .BeOfType<OkObjectResult>()
            .Subject.Value.Should()
            .BeOfType<PaginatedResponse<T>>()
            .Subject;

    [Fact]
    public async Task ListChannels_defaults_to_newest_first()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "older", Old);
        SeedChannel(db, "newer", Newer);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None
        );

        Body<AdminChannelDto>(result).Data.Select(c => c.Login).Should().Equal("newer", "older");
    }

    [Fact]
    public async Task ListChannels_sorted_oldest_reverses_the_order()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "older", Old);
        SeedChannel(db, "newer", Newer);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto { Sort = "oldest" },
            ct: CancellationToken.None
        );

        Body<AdminChannelDto>(result).Data.Select(c => c.Login).Should().Equal("older", "newer");
    }

    [Fact]
    public async Task ListChannels_sorted_by_name_ignores_creation_time()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "zeta", Newer);
        SeedChannel(db, "alpha", Old);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto { Sort = "name" },
            ct: CancellationToken.None
        );

        Body<AdminChannelDto>(result).Data.Select(c => c.Login).Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task An_unknown_sort_key_falls_back_to_the_default_instead_of_failing()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "older", Old);
        SeedChannel(db, "newer", Newer);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto { Sort = "'; drop table channels; --" },
            ct: CancellationToken.None
        );

        // Two expected items, passed as a collection: the params overload would read a "because" string as a
        // third expected row.
        Body<AdminChannelDto>(result)
            .Data.Select(c => c.Login)
            .Should()
            .Equal(
                ["newer", "older"],
                "an unrecognised sort must order by the default, never by the caller's string"
            );
    }

    [Fact]
    public async Task ListChannels_live_filter_excludes_offline_channels()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "onair", Newer, isLive: true);
        SeedChannel(db, "offair", Old);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None,
            isLive: true
        );

        PaginatedResponse<AdminChannelDto> body = Body<AdminChannelDto>(result);
        body.Data.Select(c => c.Login).Should().Equal("onair");
        body.Data.Should()
            .NotContain(c => c.Login == "offair", "a filter must exclude, not merely reorder");
    }

    [Fact]
    public async Task ListChannels_offline_filter_is_the_mirror_of_the_live_one()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, "onair", Newer, isLive: true);
        SeedChannel(db, "offair", Old);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None,
            isLive: false
        );

        Body<AdminChannelDto>(result).Data.Select(c => c.Login).Should().Equal("offair");
    }

    [Fact]
    public async Task ListUsers_sorted_by_name_orders_alphabetically()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedUser(db, "zeta", Newer);
        SeedUser(db, "alpha", Old);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListUsers(
            search: null,
            request: new PageRequestDto { Sort = "name" },
            ct: CancellationToken.None
        );

        Body<AdminUserDto>(result).Data.Select(u => u.Login).Should().Equal("alpha", "zeta");
    }

    [Fact]
    public async Task ListUsers_role_filter_returns_only_platform_staff()
    {
        // The filter uses the same derivation the row reports, so what an operator filters on is exactly
        // what they then read — a filter whose result disagrees with the column beside it is worse than none.
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedUser(db, "staffer", Newer, isPlatformPrincipal: true);
        SeedUser(db, "streamer", Old, isPlatformPrincipal: false);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListUsers(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None,
            role: "admin"
        );

        PaginatedResponse<AdminUserDto> body = Body<AdminUserDto>(result);
        body.Data.Select(u => u.Login).Should().Equal("staffer");
        body.Data.Should().OnlyContain(u => u.Role == "admin");
    }

    [Fact]
    public async Task ListUsers_role_filter_for_users_excludes_platform_staff()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedUser(db, "staffer", Newer, isPlatformPrincipal: true);
        SeedUser(db, "streamer", Old, isPlatformPrincipal: false);
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListUsers(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None,
            role: "user"
        );

        Body<AdminUserDto>(result).Data.Select(u => u.Login).Should().Equal("streamer");
    }
}
