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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The <c>switch</c> namespace (catalog section 7.5): <c>{switch:value|case=out|…|else}</c> picks the output of
/// the first case equal to the value (ignoring case), and <c>{switch.range:n|upTo=out|…|else}</c> the first case
/// whose limit is at least the number. A last argument without <c>=</c> is the else branch. Only the chosen
/// output renders when the case text is plain.
/// </summary>
public sealed class SwitchTemplateHelpers : LogicHelperProvider
{
    public override string Namespace => "switch";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [Row("switch", "queen", lazy: true), Row("switch.range", "hmm", lazy: true)];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count < 2)
            return null;

        string value = await LogicArguments.RenderParsedAsync(node, 0, cancellationToken);
        if (string.Equals(node.Key, "switch", StringComparison.OrdinalIgnoreCase))
            return await FirstCaseAsync(
                node,
                head => head.Equals(value, StringComparison.OrdinalIgnoreCase),
                cancellationToken
            );

        return LogicArguments.TryNumber(value, out double number)
            ? await FirstCaseAsync(
                node,
                head => LogicArguments.TryNumber(head, out double limit) ? number <= limit : null,
                cancellationToken
            )
            : null;
    }

    /// <summary>
    /// Walks the case arguments in order. <paramref name="isMatch"/> is null for a case head it cannot read,
    /// which makes the whole token print as written.
    /// </summary>
    private static async ValueTask<string?> FirstCaseAsync(
        HelperNode node,
        Func<string, bool?> isMatch,
        CancellationToken cancellationToken
    )
    {
        int last = node.Arguments.Count - 1;
        for (int i = 1; i <= last; i++)
        {
            SplitArgument? entry = await LogicArguments.SplitAsync(node, i, '=', cancellationToken);
            if (entry is null)
                return i == last
                    ? await LogicArguments.RenderAsync(node, i, cancellationToken)
                    : null;

            bool? matches = isMatch(entry.Value.Head);
            if (matches is null)
                return null;
            if (matches.Value)
                return await entry.Value.Tail();
        }
        return "";
    }
}
