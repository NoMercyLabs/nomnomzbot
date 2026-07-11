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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the viewer-report subsystem (moderation.md J.8): a viewer files a report (the reported chatter is
/// get-or-created as a user, the row lands open), a moderator lists the queue by status, and resolves each —
/// dismiss / escalate — with the acting moderator + timestamp recorded. Every assertion is on the persisted state
/// and the resolved id, and a bad reason / status / action fails loudly instead of writing junk.
/// </summary>
public sealed class ViewerReportServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");
    private static string BroadcasterId => Tenant.ToString();
    private static readonly Guid ReportedUserGuid = Guid.Parse(
        "019f2900-0000-7000-8000-000000000001"
    );

    private static async Task<(
        ViewerReportService Service,
        ModerationServiceTestDbContext Db,
        IUserService Users
    )> BuildAsync()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        ReportedUserGuid.ToString(),
                        "griefer",
                        "Griefer",
                        null,
                        null,
                        default,
                        default
                    )
                )
            );

        ViewerReportService service = new(
            db,
            users,
            Substitute.For<IEventBus>(),
            TimeProvider.System
        );
        return (service, db, users);
    }

    private static FileViewerReportRequest Report(string reason) =>
        new()
        {
            ReportedTwitchUserId = "5005",
            ReportedUsername = "griefer",
            Reason = reason,
        };

    [Fact]
    public async Task FileReportAsync_ResolvesTheReportedUser_AndStoresAnOpenReport()
    {
        (ViewerReportService service, ModerationServiceTestDbContext db, IUserService users) =
            await BuildAsync();

        Result<ViewerReportDto> result = await service.FileReportAsync(
            BroadcasterId,
            Report("  spamming links  "),
            reporterUserId: Guid.NewGuid().ToString()
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("open");
        result.Value.Reason.Should().Be("spamming links"); // trimmed
        result.Value.ReportedTwitchUserId.Should().Be("5005");

        // Actually persisted, tied to the get-or-created user.
        ViewerReport stored = await db.ViewerReports.SingleAsync();
        stored.ReportedUserId.Should().Be(ReportedUserGuid);
        stored.BroadcasterId.Should().Be(Tenant);
        stored.Status.Should().Be("open");
        await users
            .Received(1)
            .GetOrCreateAsync(
                "5005",
                "griefer",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task FileReportAsync_RejectsEmptyReason_WithoutWriting()
    {
        (ViewerReportService service, ModerationServiceTestDbContext db, _) = await BuildAsync();

        Result<ViewerReportDto> result = await service.FileReportAsync(
            BroadcasterId,
            Report("   "),
            reporterUserId: null
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.ViewerReports.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListReportsAsync_FiltersByStatus_AndRejectsAnUnknownStatus()
    {
        (ViewerReportService service, _, _) = await BuildAsync();
        await service.FileReportAsync(BroadcasterId, Report("one"), null);

        Result<List<ViewerReportDto>> open = await service.ListReportsAsync(BroadcasterId, "open");
        open.IsSuccess.Should().BeTrue();
        open.Value.Should().ContainSingle();

        Result<List<ViewerReportDto>> escalated = await service.ListReportsAsync(
            BroadcasterId,
            "escalated"
        );
        escalated.Value.Should().BeEmpty();

        Result<List<ViewerReportDto>> bad = await service.ListReportsAsync(BroadcasterId, "bogus");
        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task ResolveReportAsync_Escalate_SetsTheStatusAndResolvedTimestamp()
    {
        (ViewerReportService service, _, _) = await BuildAsync();
        Result<ViewerReportDto> filed = await service.FileReportAsync(
            BroadcasterId,
            Report("bad behaviour"),
            null
        );

        Result<ViewerReportDto> resolved = await service.ResolveReportAsync(
            BroadcasterId,
            filed.Value.Id,
            "escalate",
            resolverUserId: Guid.NewGuid().ToString()
        );

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Status.Should().Be("escalated");
        resolved.Value.ResolvedAt.Should().NotBeNull();

        // It no longer shows in the open queue.
        Result<List<ViewerReportDto>> open = await service.ListReportsAsync(BroadcasterId, "open");
        open.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveReportAsync_RejectsAnUnknownAction()
    {
        (ViewerReportService service, _, _) = await BuildAsync();
        Result<ViewerReportDto> filed = await service.FileReportAsync(
            BroadcasterId,
            Report("bad"),
            null
        );

        Result<ViewerReportDto> result = await service.ResolveReportAsync(
            BroadcasterId,
            filed.Value.Id,
            "nuke",
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
