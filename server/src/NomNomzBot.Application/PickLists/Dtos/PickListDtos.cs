// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.PickLists.Dtos;

/// <summary>Create payload for a new named pick-list. <c>Items</c> may be null/empty; entries are trimmed.</summary>
public sealed record CreatePickListRequest(string Name, string? Description, List<string> Items);

/// <summary>Edit payload — the desired full state of the list (name is renamable, all items replaced).</summary>
public sealed record UpdatePickListRequest(string Name, string? Description, List<string> Items);

/// <summary>Free-text search filter over <c>Name</c>/<c>Description</c>.</summary>
public sealed record PickListSearch(string? Term);

/// <summary>The transport/read shape of a pick-list.</summary>
public sealed record PickListDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>A single sampled entry from a pick-list — backs the dashboard's "Test" button preview.</summary>
public sealed record PickListPreviewDto(string Pick);
