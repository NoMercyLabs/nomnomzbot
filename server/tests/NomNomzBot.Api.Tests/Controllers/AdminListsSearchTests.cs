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
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-OWN08b: the admin Channels/Users lists had no search — every deployment with more than a page of tenants
/// or users made the console unusable. Proves <c>GET /api/v1/admin/channels?search=</c> and
/// <c>GET /api/v1/admin/users?search=</c> run a REAL, case-insensitive query against a seeded database and
/// return ONLY the matching rows (both the channel/user's own login AND its owner/display name are searched) —
/// not a mock asserting the query string was forwarded to a stub.
/// </summary>
public sealed class AdminListsSearchTests
{
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

        AdminController controller = new(adminService, db, Substitute.For<IDekRotationService>());
        return (controller, db);
    }

    private static Channel SeedChannel(
        AdminListsSearchTestDbContext db,
        string login,
        string ownerDisplayName
    )
    {
        Guid ownerId = Guid.NewGuid();
        db.Users.Add(
            new()
            {
                Id = ownerId,
                Username = ownerDisplayName,
                UsernameNormalized = ownerDisplayName.ToLowerInvariant(),
                DisplayName = ownerDisplayName,
            }
        );
        Channel channel = new()
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = login,
            NameNormalized = login.ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow,
        };
        db.Channels.Add(channel);
        return channel;
    }

    private static User SeedUser(AdminListsSearchTestDbContext db, string login, string displayName)
    {
        User user = new()
        {
            Id = Guid.NewGuid(),
            Username = login,
            UsernameNormalized = login.ToLowerInvariant(),
            DisplayName = displayName,
            // A platform principal is admitted by AdminService.ListUsersAsync's "real bot user" filter without
            // needing an owned channel or an AuthSession row — the least setup that reaches the search filter.
            IsPlatformPrincipal = true,
        };
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task ListChannels_search_matches_login_or_owner_display_name_case_insensitively()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, login: "pixelqueen", ownerDisplayName: "PixelQueen"); // matches via login
        SeedChannel(db, login: "alpha", ownerDisplayName: "PixelFan"); // matches via owner display name
        SeedChannel(db, login: "rockhound", ownerDisplayName: "RockHound"); // must be excluded
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: "PIXEL",
            request: new PageRequestDto(),
            ct: CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        PaginatedResponse<AdminChannelDto> body = ok
            .Value.Should()
            .BeOfType<PaginatedResponse<AdminChannelDto>>()
            .Subject;

        body.Data.Select(c => c.Login).Should().BeEquivalentTo(["pixelqueen", "alpha"]);
        body.Data.Should()
            .NotContain(
                c => c.Login == "rockhound",
                "an unmatched channel must be excluded, not merely deprioritized"
            );
    }

    [Fact]
    public async Task ListChannels_blank_search_returns_every_channel_unfiltered()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, login: "pixelqueen", ownerDisplayName: "PixelQueen");
        SeedChannel(db, login: "rockhound", ownerDisplayName: "RockHound");
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto(),
            ct: CancellationToken.None
        );

        PaginatedResponse<AdminChannelDto> body = ((OkObjectResult)result)
            .Value.Should()
            .BeOfType<PaginatedResponse<AdminChannelDto>>()
            .Subject;
        body.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListUsers_search_matches_login_or_display_name_case_insensitively()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedUser(db, login: "pixelqueen", displayName: "PixelQueen"); // matches via login
        SeedUser(db, login: "mod99", displayName: "PixelFan"); // matches via display name
        SeedUser(db, login: "rockhound", displayName: "RockHound"); // must be excluded
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListUsers(
            search: "pixel",
            request: new PageRequestDto(),
            ct: CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        PaginatedResponse<AdminUserDto> body = ok
            .Value.Should()
            .BeOfType<PaginatedResponse<AdminUserDto>>()
            .Subject;

        body.Data.Select(u => u.Login).Should().BeEquivalentTo(["pixelqueen", "mod99"]);
        body.Data.Should()
            .NotContain(
                u => u.Login == "rockhound",
                "an unmatched user must be excluded, not merely deprioritized"
            );
    }
}
