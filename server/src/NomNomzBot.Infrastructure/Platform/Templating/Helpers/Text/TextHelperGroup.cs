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
