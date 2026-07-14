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
/// A widget's full detail. The authored source and compiled bundle are NOT here — they live on append-only
/// <c>WidgetVersion</c> rows; <see cref="ActiveVersionId"/> names the version the overlay currently serves.
/// <c>Framework</c> is the source language (vue|react|svelte|vanilla); <c>Source</c> is the provenance
/// (first_party|verified_gallery|custom).
/// </summary>
public sealed record WidgetDetail(
    Guid Id,
    string Name,
    string? Description,
    string Framework,
    string Source,
    bool IsEnabled,
    string? OverlayUrl,
    Guid? ActiveVersionId,
    Guid? GalleryItemId,
    Dictionary<string, object?> Settings,
    List<string> EventSubscriptions,
    string? LastRuntimeError,
    DateTime? LastRanAt,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public sealed record CreateWidgetRequest
{
    public required string Name { get; init; }

    /// <summary>Source language of the widget: <c>vanilla</c> | <c>vue</c> | <c>react</c> | <c>svelte</c>.</summary>
    public required string Framework { get; init; }

    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
}

public sealed record UpdateWidgetRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
    public bool? IsEnabled { get; init; }
}

/// <summary>The authored source to compile-on-save into the widget's next append-only version.</summary>
public sealed record CompileWidgetRequest
{
    public required string SourceCode { get; init; }
}

/// <summary>
/// Fork source for clone-to-edit — exactly one of <see cref="GalleryItemId"/> / <see cref="InstalledWidgetId"/> is
/// set. Produces a NEW, fully-owned <c>custom</c> widget with the source copied in (independently editable).
/// </summary>
public sealed record CloneWidgetRequest
{
    public Guid? GalleryItemId { get; init; }
    public Guid? InstalledWidgetId { get; init; }
}

/// <summary>
/// The public, token-resolved overlay manifest (widgets-overlays.md §4). The single read an OBS browser source
/// needs: the channel's enabled, successfully-built widgets, each with the URL to fetch its compiled bundle, the
/// content hash (cache-bust key), its render-time trust tier, and its live settings + event subscriptions.
/// </summary>
public sealed record OverlayManifest(
    Guid ChannelId,
    string CspNonce,
    List<OverlayWidgetEntry> Widgets
);

/// <summary>
/// One widget in the overlay manifest. <see cref="TrustTier"/> (first_party | verified_community | unverified) is
/// derived from the widget's <c>Source</c> and drives the render-time CSP tier — a self-authored (custom) widget is
/// always <c>unverified</c> (fail-closed).
/// </summary>
public sealed record OverlayWidgetEntry(
    Guid WidgetId,
    string Name,
    string Framework,
    string TrustTier,
    string BundleUrl,
    string ContentHash,
    List<string> EventSubscriptions,
    Dictionary<string, object?> Settings
);

/// <summary>A widget's compiled bundle, served to the overlay host page. <see cref="Framework"/> picks the MIME type.</summary>
public sealed record OverlayBundle(string Content, string Framework, string ContentHash);

/// <summary>
/// A starter widget template the editor offers when creating a new custom widget — a working, SDK-using starting
/// point (never a blank editor). <see cref="Source"/> is the authored source to seed into the editor + compile.
/// </summary>
public sealed record WidgetTemplate(
    string Key,
    string Name,
    string Description,
    string Framework,
    string Source
);

/// <summary>A row in a widget's version history (rollback / debug list).</summary>
public sealed record WidgetVersionSummary(
    Guid Id,
    int VersionNumber,
    string BuildStatus,
    string? ContentHash,
    DateTime? CompiledAt,
    DateTime CreatedAt
);

/// <summary>
/// A single widget version in full. Carries <see cref="SourceCode"/> so the editor can load the current source to
/// edit, and <see cref="BuildError"/>/<see cref="BuildLog"/> so a failed build is inspectable. The compiled bundle
/// itself is served to overlays via the manifest, not returned here.
/// </summary>
public sealed record WidgetVersionDetail(
    Guid Id,
    Guid WidgetId,
    int VersionNumber,
    string BuildStatus,
    string? SourceCode,
    string? BuildError,
    string? BuildLog,
    string? ContentHash,
    DateTime? CompiledAt,
    DateTime CreatedAt
);
