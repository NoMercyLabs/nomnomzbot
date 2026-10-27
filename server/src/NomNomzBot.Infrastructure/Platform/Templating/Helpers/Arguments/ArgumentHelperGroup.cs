// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
