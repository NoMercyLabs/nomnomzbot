// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// The widget gallery (widgets-overlays.md §3.3/§5c). The gallery tables are GLOBAL (no tenant filter; the
/// soft-delete filter still applies). Reads are pinned to <c>ReviewStatus == "verified"</c> unless the caller
/// holds the <c>gallery:review</c> platform grant. The write side is the community import pipeline: submit
/// inserts a GitHub-pinned, unverified item (source is NEVER pulled at HEAD); review transitions its status
/// and trust tier; re-pin always forces re-review. Every transition appends an immutable
/// <see cref="WidgetGallerySubmissionEvent"/> — the tamper-evident moderation history.
/// </summary>
public partial class WidgetGalleryService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
) : IWidgetGalleryService
{
    private const string SubmittedStatus = "submitted";
    private const string InReviewStatus = "in_review";
    private const string VerifiedStatus = "verified";
    private const string RejectedStatus = "rejected";
    private const string UnverifiedTier = "unverified";
    private const string VerifiedCommunityTier = "verified_community";
    private const string GitHubSourceKind = "github";

    private static readonly string[] Frameworks = ["vue", "react", "svelte", "vanilla"];

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullCommitSha();

    public async Task<Result<PagedList<GalleryItemSummary>>> ListAsync(
        GalleryListRequest request,
        PaginationParams pagination,
        bool privileged = false,
        CancellationToken cancellationToken = default
    )
    {
        // A reviewer filtering by status sees that slice of the moderation queue; everyone else (and a
        // reviewer without a status filter) gets the public verified catalogue.
        bool queueRead = privileged && !string.IsNullOrWhiteSpace(request.ReviewStatus);
        IQueryable<WidgetGalleryItem> query = queueRead
            ? db.WidgetGalleryItems.Where(i => i.ReviewStatus == request.ReviewStatus)
            : db.WidgetGalleryItems.Where(i => i.ReviewStatus == VerifiedStatus);

        if (!string.IsNullOrWhiteSpace(request.Framework))
            query = query.Where(i => i.Framework == request.Framework);
        if (!string.IsNullOrWhiteSpace(request.TrustTier))
            query = query.Where(i => i.TrustTier == request.TrustTier);

        int total = await query.CountAsync(cancellationToken);

        List<WidgetGalleryItem> items = await query
            .OrderByDescending(i => i.InstallCount)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<GalleryItemSummary> summaries = items.Select(ToSummary).ToList();
        return Result.Success(
            new PagedList<GalleryItemSummary>(
                summaries,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<GalleryItemDetail>> GetAsync(
        string galleryItemId,
        bool privileged = false,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(galleryItemId, out Guid id))
            return Errors.NotFound<GalleryItemDetail>("WidgetGalleryItem", galleryItemId);

        WidgetGalleryItem? item = await db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == id && (privileged || i.ReviewStatus == VerifiedStatus),
            cancellationToken
        );

        if (item is null)
            return Errors.NotFound<GalleryItemDetail>("WidgetGalleryItem", galleryItemId);

        return Result.Success(ToDetail(item));
    }

    public async Task<Result<GalleryItemDetail>> SubmitAsync(
        Guid submitterUserId,
        SubmitGalleryItemRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255)
            return Result.Failure<GalleryItemDetail>(
                "A name of at most 255 characters is required.",
                "VALIDATION_FAILED"
            );
        if (!Frameworks.Contains(request.Framework, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<GalleryItemDetail>(
                "Framework must be vue, react, svelte, or vanilla.",
                "VALIDATION_FAILED"
            );
        Result<string> repoUrl = NormalizeGitHubUrl(request.GitHubRepoUrl);
        if (repoUrl.IsFailure)
            return Result.Failure<GalleryItemDetail>(repoUrl.ErrorMessage, repoUrl.ErrorCode);
        Result<string> sha = NormalizeCommitSha(request.PinnedCommitSha);
        if (sha.IsFailure)
            return Result.Failure<GalleryItemDetail>(sha.ErrorMessage, sha.ErrorCode);

        // Snapshot the submitter's identity so the moderation queue stays readable even if the account
        // is later renamed or removed.
        User? submitter = await db.Users.FirstOrDefaultAsync(
            u => u.Id == submitterUserId,
            cancellationToken
        );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        WidgetGalleryItem item = new()
        {
            SubmitterUserId = submitterUserId,
            SubmitterTwitchUserId = submitter?.TwitchUserId,
            SubmitterDisplayNameSnapshot = submitter?.DisplayName ?? submitter?.Username,
            Name = request.Name.Trim(),
            Description = request.Description,
            Framework = request.Framework.ToLowerInvariant(),
            TrustTier = UnverifiedTier,
            SourceKind = GitHubSourceKind,
            GitHubRepoUrl = repoUrl.Value,
            PinnedCommitSha = sha.Value,
            PinnedTag = request.PinnedTag,
            ReviewStatus = SubmittedStatus,
            AvailableInSaaS = false,
        };
        db.WidgetGalleryItems.Add(item);
        db.WidgetGallerySubmissionEvents.Add(
            HistoryRow(item.Id, null, SubmittedStatus, submitterUserId, null, null, now)
        );
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetail(item));
    }

    public async Task<Result<GalleryItemDetail>> ReviewAsync(
        Guid reviewerUserId,
        Guid galleryItemId,
        ReviewGalleryItemRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.ReviewStatus is not (InReviewStatus or VerifiedStatus or RejectedStatus))
            return Result.Failure<GalleryItemDetail>(
                "Review status must be in_review, verified, or rejected.",
                "VALIDATION_FAILED"
            );

        WidgetGalleryItem? item = await db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == galleryItemId,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<GalleryItemDetail>(
                "WidgetGalleryItem",
                galleryItemId.ToString()
            );
        if (item.SourceKind != GitHubSourceKind)
            return Result.Failure<GalleryItemDetail>(
                "First-party catalogue items are owned by the seeder and cannot be reviewed.",
                "FIRST_PARTY_IMMUTABLE"
            );

        string fromStatus = item.ReviewStatus;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        item.ReviewStatus = request.ReviewStatus;
        // The trust tier tracks the verdict — verified earns the community tier, anything else is
        // fail-closed unverified (and never SaaS-served).
        bool verified = request.ReviewStatus == VerifiedStatus;
        item.TrustTier = verified ? VerifiedCommunityTier : UnverifiedTier;
        item.AvailableInSaaS = verified && request.AvailableInSaaS;
        item.ReviewedByUserId = reviewerUserId;
        item.ReviewNotes = request.ReviewNotes;
        item.ReviewedAt = now;

        db.WidgetGallerySubmissionEvents.Add(
            HistoryRow(
                item.Id,
                fromStatus,
                request.ReviewStatus,
                reviewerUserId,
                null,
                request.ReviewNotes,
                now
            )
        );
        await db.SaveChangesAsync(cancellationToken);

        await PublishStatusChangedAsync(
            item.Id,
            fromStatus,
            request.ReviewStatus,
            null,
            reviewerUserId,
            cancellationToken
        );
        return Result.Success(ToDetail(item));
    }

    public async Task<Result<GalleryItemDetail>> UpdatePinAsync(
        Guid reviewerUserId,
        Guid galleryItemId,
        UpdatePinRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Result<string> sha = NormalizeCommitSha(request.PinnedCommitSha);
        if (sha.IsFailure)
            return Result.Failure<GalleryItemDetail>(sha.ErrorMessage, sha.ErrorCode);

        WidgetGalleryItem? item = await db.WidgetGalleryItems.FirstOrDefaultAsync(
            i => i.Id == galleryItemId,
            cancellationToken
        );
        if (item is null)
            return Errors.NotFound<GalleryItemDetail>(
                "WidgetGalleryItem",
                galleryItemId.ToString()
            );
        if (item.SourceKind != GitHubSourceKind)
            return Result.Failure<GalleryItemDetail>(
                "First-party catalogue items are owned by the seeder and cannot be re-pinned.",
                "FIRST_PARTY_IMMUTABLE"
            );

        string fromStatus = item.ReviewStatus;
        DateTime now = clock.GetUtcNow().UtcDateTime;
        item.PinnedCommitSha = sha.Value;
        item.PinnedTag = request.PinnedTag;
        // New pin = new, unreviewed code: back through review, fail-closed out of the public catalogue.
        item.ReviewStatus = InReviewStatus;
        item.TrustTier = UnverifiedTier;
        item.AvailableInSaaS = false;

        db.WidgetGallerySubmissionEvents.Add(
            HistoryRow(
                item.Id,
                fromStatus,
                InReviewStatus,
                reviewerUserId,
                sha.Value,
                request.Note,
                now
            )
        );
        await db.SaveChangesAsync(cancellationToken);

        await PublishStatusChangedAsync(
            item.Id,
            fromStatus,
            InReviewStatus,
            sha.Value,
            reviewerUserId,
            cancellationToken
        );
        return Result.Success(ToDetail(item));
    }

    // ── Internals ──

    private static WidgetGallerySubmissionEvent HistoryRow(
        Guid galleryItemId,
        string? fromStatus,
        string toStatus,
        Guid changedByUserId,
        string? newPinnedCommitSha,
        string? note,
        DateTime now
    ) =>
        new()
        {
            GalleryItemId = galleryItemId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ChangedByUserId = changedByUserId,
            NewPinnedCommitSha = newPinnedCommitSha,
            Note = note,
            OccurredAt = now,
            CreatedAt = now,
        };

    private Task PublishStatusChangedAsync(
        Guid galleryItemId,
        string fromStatus,
        string toStatus,
        string? newPinnedCommitSha,
        Guid changedByUserId,
        CancellationToken cancellationToken
    ) =>
        eventBus.PublishAsync(
            new WidgetGalleryItemStatusChangedEvent
            {
                // The gallery is GLOBAL — Guid.Empty is the platform-plane sentinel.
                BroadcasterId = Guid.Empty,
                GalleryItemId = galleryItemId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                NewPinnedCommitSha = newPinnedCommitSha,
                ChangedByUserId = changedByUserId,
            },
            cancellationToken
        );

    /// <summary>Only <c>https://github.com/{owner}/{repo}</c> shapes pass; the URL is canonicalized (no .git, no trailing junk).</summary>
    private static Result<string> NormalizeGitHubUrl(string raw)
    {
        if (
            !Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    ? uri.Host[4..]
                    : uri.Host,
                "github.com",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return Result.Failure<string>(
                "The repository must be an https://github.com URL.",
                "VALIDATION_FAILED"
            );

        string[] segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments[0].Length == 0 || segments[1].Length == 0)
            return Result.Failure<string>(
                "The GitHub URL must point at a repository (github.com/{owner}/{repo}).",
                "VALIDATION_FAILED"
            );

        string repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];
        return Result.Success($"https://github.com/{segments[0]}/{repo}");
    }

    /// <summary>Only a FULL 40-hex commit sha pins a submission — short shas and branch names are refused.</summary>
    private static Result<string> NormalizeCommitSha(string raw)
    {
        string normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return FullCommitSha().IsMatch(normalized)
            ? Result.Success(normalized)
            : Result.Failure<string>(
                "The pinned commit must be a full 40-character hex sha.",
                "VALIDATION_FAILED"
            );
    }

    private static GalleryItemSummary ToSummary(WidgetGalleryItem i) =>
        new(
            i.Id,
            i.Name,
            i.Description,
            i.Framework,
            i.TrustTier,
            i.InstallCount,
            i.AvailableInSaaS
        );

    private static GalleryItemDetail ToDetail(WidgetGalleryItem i) =>
        new(
            i.Id,
            i.Name,
            i.Description,
            i.Framework,
            i.TrustTier,
            i.InstallCount,
            i.AvailableInSaaS,
            i.SourceKind,
            new Dictionary<string, object>(i.DefaultSettings),
            [.. i.DefaultEventSubscriptions],
            i.SourceCode,
            i.GitHubRepoUrl,
            i.PinnedCommitSha,
            i.PinnedTag,
            i.ReviewStatus,
            i.ReviewNotes,
            i.ReviewedAt,
            i.CreatedAt
        );
}
