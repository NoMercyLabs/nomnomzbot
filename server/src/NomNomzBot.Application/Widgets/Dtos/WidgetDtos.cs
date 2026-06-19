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
    DateTime UpdatedAt
);

public sealed record CreateWidgetRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
}

public sealed record UpdateWidgetRequest
{
    public string? Name { get; init; }
    public Dictionary<string, object?>? Settings { get; init; }
    public List<string>? EventSubscriptions { get; init; }
    public bool? IsEnabled { get; init; }
}
