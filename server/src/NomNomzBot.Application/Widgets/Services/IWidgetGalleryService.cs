// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The widget gallery (widgets-overlays.md §3.3/§5c). Reads are anonymous and GLOBAL (no tenant scope), pinned
/// to <c>ReviewStatus == "verified"</c> so submissions never leak — a caller holding the <c>gallery:review</c>
/// platform grant may read the full moderation queue. The write side is the community import pipeline: any
/// authenticated user submits a GitHub-pinned widget; review and re-pin are platform-IAM gated, every
/// transition appends an immutable <c>WidgetGallerySubmissionEvent</c>.
/// </summary>
public interface IWidgetGalleryService
{
    /// <summary>
    /// List the gallery, most-installed then newest first, applying the optional framework/trust-tier
    /// filters. <paramref name="privileged"/> (the <c>gallery:review</c> caller) additionally honors the
    /// <see cref="GalleryListRequest.ReviewStatus"/> queue filter and sees non-verified items when filtering;
    /// everyone else is pinned to verified.
    /// </summary>
    Task<Result<PagedList<GalleryItemSummary>>> ListAsync(
        GalleryListRequest request,
        PaginationParams pagination,
        bool privileged = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get one gallery item in full (incl. its source, for preview). Non-verified items resolve only for a
    /// <paramref name="privileged"/> caller; otherwise NOT_FOUND.
    /// </summary>
    Task<Result<GalleryItemDetail>> GetAsync(
        string galleryItemId,
        bool privileged = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Submit a community widget: validates + normalizes the GitHub URL and the full 40-hex pinned commit
    /// (never pulls HEAD), inserts the item (<c>submitted</c>/<c>unverified</c>, submitter snapshotted), and
    /// appends the <c>null→submitted</c> history row.
    /// </summary>
    Task<Result<GalleryItemDetail>> SubmitAsync(
        Guid submitterUserId,
        SubmitGalleryItemRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Review a submission: transitions <c>ReviewStatus</c> (<c>in_review</c>/<c>verified</c>/<c>rejected</c>),
    /// grants <c>TrustTier=verified_community</c> on verify (and drops it otherwise — fail-closed), records
    /// reviewer/notes/timestamp, appends the history row, and publishes
    /// <c>WidgetGalleryItemStatusChangedEvent</c>. First-party (in-repo) items are immutable here.
    /// </summary>
    Task<Result<GalleryItemDetail>> ReviewAsync(
        Guid reviewerUserId,
        Guid galleryItemId,
        ReviewGalleryItemRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Re-pin a submission to a new commit/tag. ALWAYS forces <c>ReviewStatus</c> back to <c>in_review</c>
    /// (new code is unreviewed code — never auto-pulled, never auto-trusted), appends the history row with
    /// the new sha, and publishes the status-changed event.
    /// </summary>
    Task<Result<GalleryItemDetail>> UpdatePinAsync(
        Guid reviewerUserId,
        Guid galleryItemId,
        UpdatePinRequest request,
        CancellationToken cancellationToken = default
    );
}
