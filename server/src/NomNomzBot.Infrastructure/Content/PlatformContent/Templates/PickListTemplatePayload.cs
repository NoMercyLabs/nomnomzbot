// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
