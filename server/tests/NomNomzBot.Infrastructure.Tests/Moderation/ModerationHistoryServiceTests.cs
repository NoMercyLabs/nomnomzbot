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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the queryable moderation-history log's read + manual-note surface (owner punch list 2026-09-08
/// §3/§12): real pagination, per-person filtering, date-range filtering, and that a manual note lands as its
/// own <c>ModerationHistoryEntryKinds.Note</c> row rather than silently no-opping.
/// </summary>
public sealed class ModerationHistoryServiceTests
{
    private static readonly Guid Channel = Guid.CreateVersion7();
    private static readonly Guid OtherChannel = Guid.CreateVersion7();
    private static readonly Guid Subject = Guid.CreateVersion7();
    private static readonly Guid OtherSubject = Guid.CreateVersion7();
    private static readonly Guid Moderator = Guid.CreateVersion7();
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ModerationHistoryService NewService(ModerationServiceTestDbContext db) =>
        new(db, new FakeTimeProvider(new(T0)));

    private static async Task SeedUsersAsync(ModerationServiceTestDbContext db)
    {
        db.Users.Add(
            new()
            {
                Id = Subject,
                TwitchUserId = "subject-twitch",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        db.Users.Add(
            new()
            {
                Id = Moderator,
                TwitchUserId = "mod-twitch",
                Username = "moduser",
                UsernameNormalized = "moduser",
                DisplayName = "ModUser",
            }
        );
        await db.SaveChangesAsync();
    }

    private static ModerationHistoryEntry Entry(
        Guid broadcasterId,
        Guid subjectUserId,
        string actionType,
        DateTime occurredAt
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            SubjectUserId = subjectUserId,
            SubjectTwitchUserId = "subject-twitch",
            ActionType = actionType,
            OccurredAt = occurredAt,
        };

    [Fact]
    public async Task GetHistoryAsync_ReturnsOnlyThisChannelsEntries_NewestFirst()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "warn", T0));
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "timeout", T0.AddHours(1)));
        db.ModerationHistoryEntries.Add(Entry(OtherChannel, Subject, "ban", T0.AddHours(2)));
        await db.SaveChangesAsync();

        ModerationHistoryService service = NewService(db);
        Result<PagedList<ModerationHistoryEntryDto>> result = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(),
            new PaginationParams(1, 25)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TotalCount.Should().Be(2, "the OtherChannel row must never leak in");
        result.Value.Items.Select(e => e.ActionType).Should().Equal("timeout", "warn");
    }

    [Fact]
    public async Task GetHistoryAsync_PagesCorrectly_AcrossMultipleRequests()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        for (int i = 0; i < 5; i++)
            db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "warn", T0.AddMinutes(i)));
        await db.SaveChangesAsync();

        ModerationHistoryService service = NewService(db);

        Result<PagedList<ModerationHistoryEntryDto>> page1 = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(),
            new PaginationParams(1, 2)
        );
        Result<PagedList<ModerationHistoryEntryDto>> page2 = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(),
            new PaginationParams(2, 2)
        );
        Result<PagedList<ModerationHistoryEntryDto>> page3 = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(),
            new PaginationParams(3, 2)
        );

        page1.Value.TotalCount.Should().Be(5);
        page1.Value.Items.Should().HaveCount(2);
        page2.Value.Items.Should().HaveCount(2);
        page3.Value.Items.Should().HaveCount(1, "5 rows at 2 per page leaves a final partial page");
        // Every id across all three pages is distinct — nothing repeated, nothing skipped.
        List<Guid> allIds =
        [
            .. page1.Value.Items.Select(e => e.Id),
            .. page2.Value.Items.Select(e => e.Id),
            .. page3.Value.Items.Select(e => e.Id),
        ];
        allIds.Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByPerson()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        db.Users.Add(
            new()
            {
                Id = OtherSubject,
                TwitchUserId = "other-twitch",
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "warn", T0));
        db.ModerationHistoryEntries.Add(Entry(Channel, OtherSubject, "ban", T0.AddHours(1)));
        await db.SaveChangesAsync();

        ModerationHistoryService service = NewService(db);
        Result<PagedList<ModerationHistoryEntryDto>> result = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(SubjectUserId: Subject),
            new PaginationParams(1, 25)
        );

        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Single().SubjectUserId.Should().Be(Subject);
    }

    [Fact]
    public async Task GetHistoryAsync_FiltersByDateRangeAndActionType()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "warn", T0));
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "ban", T0.AddDays(1)));
        db.ModerationHistoryEntries.Add(Entry(Channel, Subject, "ban", T0.AddDays(10)));
        await db.SaveChangesAsync();

        ModerationHistoryService service = NewService(db);
        Result<PagedList<ModerationHistoryEntryDto>> result = await service.GetHistoryAsync(
            Channel,
            new ModerationHistoryQuery(
                FromUtc: T0.AddHours(1),
                ToUtc: T0.AddDays(2),
                ActionType: "ban"
            ),
            new PaginationParams(1, 25)
        );

        result
            .Value.TotalCount.Should()
            .Be(1, "the warn is the wrong type and the day-10 ban is outside the range");
        result.Value.Items.Single().OccurredAt.Should().Be(T0.AddDays(1));
    }

    [Fact]
    public async Task AddNoteAsync_AppendsANoteEntry_AttributedToTheActingModerator()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        ModerationHistoryService service = NewService(db);

        Result<ModerationHistoryEntryDto> result = await service.AddNoteAsync(
            Channel,
            Subject,
            Moderator,
            "Talked to them in DMs, seems fine now."
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ActionType.Should().Be(ModerationHistoryEntryKinds.Note);
        result.Value.ModeratorUserId.Should().Be(Moderator);
        result.Value.ModeratorDisplayName.Should().Be("ModUser");
        result.Value.Reason.Should().Be("Talked to them in DMs, seems fine now.");

        ModerationHistoryEntry stored = await db.ModerationHistoryEntries.SingleAsync(e =>
            e.BroadcasterId == Channel
        );
        stored.ActionType.Should().Be(ModerationHistoryEntryKinds.Note);
        stored.SubjectUserId.Should().Be(Subject);
    }

    [Fact]
    public async Task AddNoteAsync_FailsValidation_ForBlankText()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        await SeedUsersAsync(db);
        ModerationHistoryService service = NewService(db);

        Result<ModerationHistoryEntryDto> result = await service.AddNoteAsync(
            Channel,
            Subject,
            Moderator,
            "   "
        );

        result.IsFailure.Should().BeTrue();
        (await db.ModerationHistoryEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddNoteAsync_FailsNotFound_ForASubjectWithNoLocalUser()
    {
        using ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ModerationHistoryService service = NewService(db);

        Result<ModerationHistoryEntryDto> result = await service.AddNoteAsync(
            Channel,
            Guid.CreateVersion7(),
            Moderator,
            "a note about a ghost"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
