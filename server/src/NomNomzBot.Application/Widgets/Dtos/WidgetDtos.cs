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
