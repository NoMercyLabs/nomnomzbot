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

public sealed record WidgetListItem(
    string Id,
    string Name,
    string Type,
    bool IsEnabled,
    DateTime CreatedAt
);

public sealed record WidgetDetail(
    string Id,
    string Name,
    string Type,
    bool IsEnabled,
    string? OverlayUrl,
    Dictionary<string, object?> Settings,
    List<string> EventSubscriptions,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // The widget's authored source (HTML/CSS/JS for a custom overlay). Null for template-driven widgets that
    // carry no hand-written code. Read and written by the dashboard's custom-widget code editor.
    string? CustomCode
);

public sealed record CreateWidgetRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }

    /// <summary>Optional starter source for a custom widget; null leaves the widget code-less.</summary>
    public string? CustomCode { get; init; }
}

public sealed record UpdateWidgetRequest
{
    public string? Name { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
    public bool? IsEnabled { get; init; }

    /// <summary>
    /// The widget's authored source. A partial patch: null leaves the stored code untouched (so a rename or
    /// toggle never clears it); a non-null value — including an empty string — replaces it.
    /// </summary>
    public string? CustomCode { get; init; }
}
