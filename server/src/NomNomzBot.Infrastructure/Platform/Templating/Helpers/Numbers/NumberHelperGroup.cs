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
            return row.Render(
                ViewerCountLimit.Within(
                    node.Arguments,
                    row.AmplifierArgument,
                    scope.TextCountLimit
                ),
                NumberCulture.For(scope.Language)
            );
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
