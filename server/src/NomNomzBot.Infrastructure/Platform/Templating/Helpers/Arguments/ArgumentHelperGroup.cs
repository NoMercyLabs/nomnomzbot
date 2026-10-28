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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>The shared half of every group: builds the registry rows and dispatches a node to the row that accepts its key.</summary>
internal abstract class ArgumentHelperGroup : IArgumentHelperGroup
{
    private readonly IReadOnlyList<ArgumentRow> _rows;

    protected ArgumentHelperGroup(params ArgumentRow[] rows)
    {
        _rows = rows;
        Entries = [.. rows.Select(r => r.ToEntry())];
    }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public string? TryRender(HelperNode node, CommandWords words)
    {
        foreach (ArgumentRow row in _rows)
        {
            if (row.Accepts(node.Key))
                return row.Render(new ArgumentCall(node, words));
        }
        return null;
    }
}
