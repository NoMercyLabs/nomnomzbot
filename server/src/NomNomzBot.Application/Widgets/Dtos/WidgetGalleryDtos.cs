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
/// A single verified gallery item in full: the summary fields plus everything the dashboard's install/clone UI needs
/// to preview and pre-configure — the item's <see cref="SourceCode"/> (for the preview pane), its declared
/// <see cref="DefaultSettings"/> / <see cref="DefaultEventSubscriptions"/> (applied on install), and its
/// <see cref="SourceKind"/> provenance (<c>in_repo</c> | <c>github</c>).
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
    string? SourceCode
);

/// <summary>
/// Optional filters for the public gallery browse list. Both narrow within the verified catalogue the read exposes —
/// they never widen it to unverified/in-review submissions.
/// </summary>
public sealed record GalleryListRequest
{
    /// <summary>Restrict to one source language: <c>vue</c> | <c>react</c> | <c>svelte</c> | <c>vanilla</c>.</summary>
    public string? Framework { get; init; }

    /// <summary>Restrict to one trust tier: <c>first_party</c> | <c>verified_community</c> | <c>unverified</c>.</summary>
    public string? TrustTier { get; init; }
}
