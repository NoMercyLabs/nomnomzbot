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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the gallery's community import pipeline (widgets-overlays.md §3.3): a submission lands
/// <c>submitted</c>/<c>unverified</c> with a normalized GitHub pin, a snapshot of the submitter, and the
/// <c>null→submitted</c> history row — and stays OFF the public list; verifying grants
/// <c>verified_community</c> and surfaces it publicly (rejecting does not); re-pinning ALWAYS forces the item
/// back through review and out of the catalogue. Every transition appends an immutable
/// <c>WidgetGallerySubmissionEvent</c> and publishes the status-changed domain event.
/// </summary>
public sealed class WidgetGalleryWriteTests
{
    private static readonly Guid Submitter = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid Reviewer = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
    private static readonly PaginationParams FirstPage = new(1, 25);
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 17, 16, 0, 0, TimeSpan.Zero)
    );

    private static SubmitGalleryItemRequest Submission(
        string url = "https://www.github.com/acme/confetti-widget.git",
        string sha = "0123456789ABCDEF0123456789abcdef01234567"
    ) =>
        new()
        {
            Name = "Confetti",
            Framework = "vue",
            GitHubRepoUrl = url,
            PinnedCommitSha = sha,
            PinnedTag = "v1.0.0",
            Description = "Confetti bursts on alerts.",
        };

    private static (
        WidgetGalleryService Service,
        WidgetTestDbContext Db,
        RecordingEventBus Bus
    ) New(WidgetSqliteTestDatabase database)
    {
        WidgetTestDbContext db = database.NewContext();
        if (!db.Users.Any(u => u.Id == Submitter))
        {
            db.Users.Add(
                new User
                {
                    Id = Submitter,
                    TwitchUserId = "12345",
                    Username = "confetti_dev",
                    UsernameNormalized = "confetti_dev",
                    DisplayName = "ConfettiDev",
                }
            );
            db.SaveChanges();
        }
        RecordingEventBus bus = new();
        return (new WidgetGalleryService(db, bus, Clock), db, bus);
    }

    [Fact]
    public async Task Submit_lands_unverified_with_a_normalized_pin_a_snapshot_and_the_first_history_row()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, WidgetTestDbContext db, _) = New(database);

        Result<GalleryItemDetail> submitted = await service.SubmitAsync(Submitter, Submission());

        submitted.IsSuccess.Should().BeTrue(submitted.ErrorMessage);
        GalleryItemDetail detail = submitted.Value;
        detail.ReviewStatus.Should().Be("submitted");
        detail.TrustTier.Should().Be("unverified");
        detail.AvailableInSaaS.Should().BeFalse();
        detail
            .GitHubRepoUrl.Should()
            .Be("https://github.com/acme/confetti-widget", "www + .git are normalized away");
        detail
            .PinnedCommitSha.Should()
            .Be("0123456789abcdef0123456789abcdef01234567", "the sha is lower-cased");

        WidgetGalleryItem row = await db.WidgetGalleryItems.SingleAsync(i => i.Id == detail.Id);
        row.SourceKind.Should().Be("github");
        row.SubmitterUserId.Should().Be(Submitter);
        row.SubmitterDisplayNameSnapshot.Should().Be("ConfettiDev");
        row.SubmitterTwitchUserId.Should().Be("12345");

        WidgetGallerySubmissionEvent history = await db.WidgetGallerySubmissionEvents.SingleAsync(
            e => e.GalleryItemId == detail.Id
        );
        history.FromStatus.Should().BeNull();
        history.ToStatus.Should().Be("submitted");
        history.ChangedByUserId.Should().Be(Submitter);

        // A submission never leaks onto the public surface; the reviewer's queue read sees it.
        Result<PagedList<GalleryItemSummary>> publicList = await service.ListAsync(
            new GalleryListRequest(),
            FirstPage
        );
        publicList.Value.Items.Should().BeEmpty();
        Result<PagedList<GalleryItemSummary>> queue = await service.ListAsync(
            new GalleryListRequest { ReviewStatus = "submitted" },
            FirstPage,
            privileged: true
        );
        queue.Value.Items.Should().ContainSingle(i => i.Id == detail.Id);
        (await service.GetAsync(detail.Id.ToString())).IsFailure.Should().BeTrue();
        (await service.GetAsync(detail.Id.ToString(), privileged: true))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Theory]
    [InlineData("https://gitlab.com/acme/widget", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData("https://github.com/acme", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData("https://github.com/acme/widget", "abc123")]
    [InlineData("https://github.com/acme/widget", "main")]
    public async Task Submit_refuses_a_non_github_repo_or_a_partial_pin(string url, string sha)
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, _, _) = New(database);

        Result<GalleryItemDetail> submitted = await service.SubmitAsync(
            Submitter,
            Submission(url, sha)
        );

        submitted.IsFailure.Should().BeTrue();
        submitted.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Verifying_grants_the_community_tier_surfaces_it_publicly_and_records_everything()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, WidgetTestDbContext db, RecordingEventBus bus) = New(
            database
        );
        Guid itemId = (await service.SubmitAsync(Submitter, Submission())).Value.Id;

        Result<GalleryItemDetail> reviewed = await service.ReviewAsync(
            Reviewer,
            itemId,
            new ReviewGalleryItemRequest
            {
                ReviewStatus = "verified",
                ReviewNotes = "Clean source, no network calls.",
                AvailableInSaaS = true,
            }
        );

        reviewed.IsSuccess.Should().BeTrue(reviewed.ErrorMessage);
        reviewed.Value.TrustTier.Should().Be("verified_community");
        reviewed.Value.AvailableInSaaS.Should().BeTrue();
        reviewed.Value.ReviewNotes.Should().Be("Clean source, no network calls.");
        reviewed.Value.ReviewedAt.Should().NotBeNull();

        // The verdict is durable and audited.
        WidgetGalleryItem row = await db.WidgetGalleryItems.SingleAsync(i => i.Id == itemId);
        row.ReviewedByUserId.Should().Be(Reviewer);
        List<WidgetGallerySubmissionEvent> history = await db
            .WidgetGallerySubmissionEvents.Where(e => e.GalleryItemId == itemId)
            .ToListAsync();
        history.Should().HaveCount(2);
        WidgetGallerySubmissionEvent verdictRow = history.Single(e => e.ToStatus == "verified");
        verdictRow.FromStatus.Should().Be("submitted");
        verdictRow.ChangedByUserId.Should().Be(Reviewer);

        WidgetGalleryItemStatusChangedEvent published = bus
            .Published.OfType<WidgetGalleryItemStatusChangedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.FromStatus.Should().Be("submitted");
        published.ToStatus.Should().Be("verified");
        published.ChangedByUserId.Should().Be(Reviewer);

        // ...and the widget is now installable: it appears on the anonymous public list.
        Result<PagedList<GalleryItemSummary>> publicList = await service.ListAsync(
            new GalleryListRequest(),
            FirstPage
        );
        publicList.Value.Items.Should().ContainSingle(i => i.Id == itemId);
    }

    [Fact]
    public async Task Rejecting_keeps_the_item_unverified_and_off_the_public_list()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, _, _) = New(database);
        Guid itemId = (await service.SubmitAsync(Submitter, Submission())).Value.Id;

        Result<GalleryItemDetail> rejected = await service.ReviewAsync(
            Reviewer,
            itemId,
            new ReviewGalleryItemRequest
            {
                ReviewStatus = "rejected",
                ReviewNotes = "Loads remote script.",
                AvailableInSaaS = true,
            }
        );

        rejected.IsSuccess.Should().BeTrue(rejected.ErrorMessage);
        rejected.Value.TrustTier.Should().Be("unverified");
        rejected.Value.AvailableInSaaS.Should().BeFalse("a rejected item is never SaaS-served");
        Result<PagedList<GalleryItemSummary>> publicList = await service.ListAsync(
            new GalleryListRequest(),
            FirstPage
        );
        publicList.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Re_pinning_forces_the_item_back_through_review_and_out_of_the_catalogue()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, WidgetTestDbContext db, RecordingEventBus bus) = New(
            database
        );
        Guid itemId = (await service.SubmitAsync(Submitter, Submission())).Value.Id;
        await service.ReviewAsync(
            Reviewer,
            itemId,
            new ReviewGalleryItemRequest { ReviewStatus = "verified", AvailableInSaaS = true }
        );
        string newSha = "fedcba9876543210fedcba9876543210fedcba98";

        Result<GalleryItemDetail> pinned = await service.UpdatePinAsync(
            Reviewer,
            itemId,
            new UpdatePinRequest
            {
                PinnedCommitSha = newSha,
                PinnedTag = "v1.1.0",
                Note = "Upstream fix release.",
            }
        );

        pinned.IsSuccess.Should().BeTrue(pinned.ErrorMessage);
        pinned.Value.PinnedCommitSha.Should().Be(newSha);
        pinned.Value.ReviewStatus.Should().Be("in_review", "new code is unreviewed code");
        pinned.Value.TrustTier.Should().Be("unverified");
        pinned.Value.AvailableInSaaS.Should().BeFalse();

        WidgetGallerySubmissionEvent pinRow = await db.WidgetGallerySubmissionEvents.SingleAsync(
            e => e.GalleryItemId == itemId && e.NewPinnedCommitSha == newSha
        );
        pinRow.FromStatus.Should().Be("verified");
        pinRow.ToStatus.Should().Be("in_review");
        pinRow.NewPinnedCommitSha.Should().Be(newSha);
        bus.Published.OfType<WidgetGalleryItemStatusChangedEvent>()
            .Should()
            .Contain(e => e.NewPinnedCommitSha == newSha && e.ToStatus == "in_review");

        // The previously-verified item left the public catalogue with the re-pin.
        Result<PagedList<GalleryItemSummary>> publicList = await service.ListAsync(
            new GalleryListRequest(),
            FirstPage
        );
        publicList.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task First_party_catalogue_items_cannot_be_reviewed_or_re_pinned()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (WidgetGalleryService service, WidgetTestDbContext db, _) = New(database);
        WidgetGalleryItem firstParty = new()
        {
            Name = "Alerts",
            Framework = "vue",
            TrustTier = "first_party",
            SourceKind = "in_repo",
            NaturalKey = "alerts",
            ReviewStatus = "verified",
            AvailableInSaaS = true,
        };
        db.WidgetGalleryItems.Add(firstParty);
        await db.SaveChangesAsync();

        Result<GalleryItemDetail> reviewed = await service.ReviewAsync(
            Reviewer,
            firstParty.Id,
            new ReviewGalleryItemRequest { ReviewStatus = "rejected" }
        );
        Result<GalleryItemDetail> pinned = await service.UpdatePinAsync(
            Reviewer,
            firstParty.Id,
            new UpdatePinRequest { PinnedCommitSha = "0123456789abcdef0123456789abcdef01234567" }
        );

        reviewed.ErrorCode.Should().Be("FIRST_PARTY_IMMUTABLE");
        pinned.ErrorCode.Should().Be("FIRST_PARTY_IMMUTABLE");
        (await db.WidgetGalleryItems.SingleAsync(i => i.Id == firstParty.Id))
            .ReviewStatus.Should()
            .Be("verified", "the seeder-owned row is untouched");
    }
}
