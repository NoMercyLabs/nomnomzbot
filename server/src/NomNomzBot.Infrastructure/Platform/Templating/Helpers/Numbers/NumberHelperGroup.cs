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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>One responsibility inside a number namespace (rounding, percent, words, bars, ...).</summary>
internal interface INumberHelperGroup
{
    /// <summary>The registry rows this group owns.</summary>
    IReadOnlyList<TemplateHelperEntry> Entries { get; }

    /// <summary>The rendered text of the node; null when the key is not this group's or an argument is bad.</summary>
    string? TryRender(HelperNode node, TemplateRenderScope scope);
}

/// <summary>
/// The shared half of every group: builds the registry rows and dispatches a node to its row by key. A number
/// that leaves the range of <see cref="decimal"/> is a bad argument like any other, so the one place that
/// catches the overflow is here, not in each row.
/// </summary>
internal abstract class NumberHelperGroup : INumberHelperGroup
{
    private readonly Dictionary<string, NumberHelperRow> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    protected NumberHelperGroup(params NumberHelperRow[] rows)
    {
        foreach (NumberHelperRow row in rows)
            _rows.Add(row.Key, row);
        Entries = [.. rows.Select(r => r.ToEntry())];
    }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public string? TryRender(HelperNode node, TemplateRenderScope scope)
    {
        if (!_rows.TryGetValue(node.Key, out NumberHelperRow? row))
            return null;

        try
        {
            return row.Render(node.Arguments, NumberCulture.For(scope.Language));
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
