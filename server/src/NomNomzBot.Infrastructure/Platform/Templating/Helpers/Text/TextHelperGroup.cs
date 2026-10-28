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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>The shared half of every group: builds the registry rows and dispatches a node to its row by key.</summary>
internal abstract class TextHelperGroup : ITextHelperGroup
{
    private readonly Dictionary<string, TextHelperRow> _rows = new(
        StringComparer.OrdinalIgnoreCase
    );

    protected TextHelperGroup(params TextHelperRow[] rows)
    {
        foreach (TextHelperRow row in rows)
            _rows.Add(row.Key, row);
        Entries = [.. rows.Select(r => r.ToEntry())];
    }

    public IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public string? TryRender(HelperNode node, TemplateRenderScope scope) =>
        _rows.TryGetValue(node.Key, out TextHelperRow? row)
            ? row.Render(WithinLimit(node.Arguments, row.AmplifierArgument, scope.TextCountLimit))
            : null;

    /// <summary>
    /// The arguments with the count at <paramref name="index"/> cut down to <paramref name="limit"/>, so a viewer's
    /// <c>{text.repeat:30|ha}</c> gives 20 copies instead of a refusal. No limit or no count argument: unchanged.
    /// </summary>
    private static IReadOnlyList<string> WithinLimit(
        IReadOnlyList<string> args,
        int? index,
        int? limit
    )
    {
        if (index is null || limit is null || index >= args.Count)
            return args;

        bool isCount = TextArguments.TryCount(args[index.Value], out int count);
        if (!isCount || count <= limit)
            return args;

        List<string> cut = [.. args];
        cut[index.Value] = limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return cut;
    }
}
