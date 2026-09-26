// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.PickLists.Entities;

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// The <c>pick_list</c> template payload — the portable part of a <see cref="PickList"/>. The name is also the
/// <c>{list.pick.name}</c> key chat templates use, so install keeps it verbatim.
/// </summary>
public sealed record PickListTemplatePayload
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public List<string> Items { get; init; } = [];

    public static PickListTemplatePayload FromEntity(PickList row) =>
        new()
        {
            Name = row.Name,
            Description = row.Description,
            Items = [.. row.Items],
        };

    public string ComputeHash() => PlatformTemplateJson.Hash(this);
}
