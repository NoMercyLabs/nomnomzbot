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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// S-OWN08b: the admin Channels list had no search — every deployment with more than a page of channels made
/// the console unusable. Proves <c>GET /api/v1/admin/channels?search=</c> runs a REAL, case-insensitive query
/// against a seeded database and returns ONLY the matching rows (both the channel's own login AND its owner's
/// display name are searched) — not a mock asserting the query string was forwarded to a stub.
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
            Substitute.For<IProcessCpuSampler>(),
            Substitute.For<IPlatformBotReadinessGate>(),
            Substitute.For<IOutboundWebhookDispatcher>(),
            Substitute.For<IScheduledPipelineService>(),
            [],
            new EventUpcasterRegistry([]),
            Substitute.For<IEventBus>()
        );

        AdminController controller = new(
            adminService,
            db,
            Substitute.For<IDekRotationService>(),
            Substitute.For<IProviderCredentialService>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IPlatformBotAdminService>(),
            Substitute.For<IIamCallerPrincipalResolverService>()
        );
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
    public async Task ListChannels_page_carries_the_total_across_all_pages()
    {
        (AdminController controller, AdminListsSearchTestDbContext db) = Build();
        SeedChannel(db, login: "pixelqueen", ownerDisplayName: "PixelQueen");
        SeedChannel(db, login: "rockhound", ownerDisplayName: "RockHound");
        SeedChannel(db, login: "alpha", ownerDisplayName: "Alpha");
        await db.SaveChangesAsync();

        IActionResult result = await controller.ListChannels(
            search: null,
            request: new PageRequestDto { Page = 1, Take = 2 },
            ct: CancellationToken.None
        );

        PaginatedResponse<AdminChannelDto> body = ((OkObjectResult)result)
            .Value.Should()
            .BeOfType<PaginatedResponse<AdminChannelDto>>()
            .Subject;
        // The pager shows "N channels" and "page 1 of 2" from the total, not from the rows on this page.
        body.Data.Should().HaveCount(2);
        body.Total.Should().Be(3);
        body.HasMore.Should().BeTrue();
        body.NextPage.Should().Be(2);
    }
}
