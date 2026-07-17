// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// A row in the public widget-gallery browse list (widgets-overlays.md §5c). The lightweight shape a channel scans
/// when choosing what to install/clone — the heavy fields (source, default config) load only on the detail read.
/// <see cref="TrustTier"/> is <c>first_party</c> | <c>verified_community</c> | <c>unverified</c>.
/// </summary>
public sealed record GalleryItemSummary(
    Guid Id,
    string Name,
    string? Description,
    string Framework,
    string TrustTier,
    int InstallCount,
    bool AvailableInSaaS
);

/// <summary>
/// A single gallery item in full: the summary fields plus everything the dashboard's install/clone UI needs
/// to preview and pre-configure — the item's <see cref="SourceCode"/> (for the preview pane), its declared
/// <see cref="DefaultSettings"/> / <see cref="DefaultEventSubscriptions"/> (applied on install), its
/// <see cref="SourceKind"/> provenance (<c>in_repo</c> | <c>github</c>) — and the review/pin fields the
/// moderation UI reads (GitHub provenance, <see cref="ReviewStatus"/>, notes, timestamps).
/// </summary>
public sealed record GalleryItemDetail(
    Guid Id,
    string Name,
    string? Description,
    string Framework,
    string TrustTier,
    int InstallCount,
    bool AvailableInSaaS,
    string SourceKind,
    Dictionary<string, object> DefaultSettings,
    List<string> DefaultEventSubscriptions,
    string? SourceCode,
    string? GitHubRepoUrl,
    string? PinnedCommitSha,
    string? PinnedTag,
    string ReviewStatus,
    string? ReviewNotes,
    DateTime? ReviewedAt,
    DateTime CreatedAt
);

/// <summary>
/// Optional filters for the gallery browse list. Framework/trust-tier narrow within the verified catalogue;
/// <see cref="ReviewStatus"/> (the reviewer's queue filter) is honored ONLY for a caller holding the
/// <c>gallery:review</c> platform grant — anonymous readers stay pinned to verified items.
/// </summary>
public sealed record GalleryListRequest
{
    /// <summary>Restrict to one source language: <c>vue</c> | <c>react</c> | <c>svelte</c> | <c>vanilla</c>.</summary>
    public string? Framework { get; init; }

    /// <summary>Restrict to one trust tier: <c>first_party</c> | <c>verified_community</c> | <c>unverified</c>.</summary>
    public string? TrustTier { get; init; }

    /// <summary>Reviewer-only: <c>submitted</c> | <c>in_review</c> | <c>verified</c> | <c>rejected</c>.</summary>
    public string? ReviewStatus { get; init; }
}

/// <summary>A community widget submission (widgets-overlays.md §3.3): GitHub-pinned source, never pulled at HEAD.</summary>
public sealed record SubmitGalleryItemRequest
{
    public required string Name { get; init; }
    public required string Framework { get; init; }
    public required string GitHubRepoUrl { get; init; }
    public required string PinnedCommitSha { get; init; }
    public string? PinnedTag { get; init; }
    public string? Description { get; init; }
}

/// <summary>A review decision: <c>in_review</c> | <c>verified</c> | <c>rejected</c>.</summary>
public sealed record ReviewGalleryItemRequest
{
    public required string ReviewStatus { get; init; }
    public string? ReviewNotes { get; init; }
    public bool AvailableInSaaS { get; init; }
}

/// <summary>A re-pin to a new commit/tag — always forces the item back through review.</summary>
public sealed record UpdatePinRequest
{
    public required string PinnedCommitSha { get; init; }
    public string? PinnedTag { get; init; }
    public string? Note { get; init; }
}
