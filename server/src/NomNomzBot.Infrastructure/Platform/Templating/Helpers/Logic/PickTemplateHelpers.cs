// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Picking;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The <c>pick</c> namespace (catalog section 7.5). <c>pick</c> and <c>pick.inline</c> draw one item through
/// <see cref="NoImmediateRepeatPicker"/> (the same item never comes twice in a row for one template);
/// <c>pick.weighted</c>, <c>pick.n</c> and <c>pick.shuffle</c> draw from the random source. The public
/// constructor uses <see cref="Random.Shared"/>; a test hands the internal one a seeded <see cref="Random"/>.
/// </summary>
public sealed class PickTemplateHelpers(Random random) : LogicHelperProvider
{
    public PickTemplateHelpers()
        : this(Random.Shared) { }

    public override string Namespace => "pick";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        Row("pick", "rock"),
        Row("pick.weighted", "common"),
        Row("pick.n", "b, a"),
        Row("pick.shuffle", "c, a, b"),
        Row("pick.inline", "a", lazy: true),
    ];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        node.Key.ToLowerInvariant() switch
        {
            "pick" => PickOne(scope, node, node.Arguments),
            "pick.inline" => await PickInlineAsync(scope, node, cancellationToken),
            "pick.weighted" => PickWeighted(node.Arguments),
            "pick.n" => PickMany(node.Arguments),
            "pick.shuffle" => node.Arguments.Count == 0 ? null : Join(Shuffled(node.Arguments)),
            _ => null,
        };

    private static string? PickOne(
        TemplateRenderScope scope,
        HelperNode node,
        IReadOnlyList<string> items
    ) =>
        items.Count == 0
            ? null
            : NoImmediateRepeatPicker.Pick(
                items,
                $"template.pick:{scope.BroadcasterId}:{node.Raw}"
            );

    /// <summary>
    /// <c>pick.inline:a;b;c</c>: the items are split at <c>;</c>, so a <c>|</c> may stay inside an item. Plain
    /// items are read from the source and only the chosen one is used; an item with a token inside renders the
    /// whole list first (one render of the arguments), then draws.
    /// </summary>
    private static async ValueTask<string?> PickInlineAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count == 0)
            return null;

        string raw = string.Join("|", node.Arguments);
        if (LogicArguments.IsPlainText(raw))
            return PickOne(scope, node, SplitItems(raw)) is { } plain
                ? LogicArguments.Unescape(plain).Trim()
                : null;

        List<string> rendered = [];
        for (int i = 0; i < node.Arguments.Count; i++)
            rendered.Add(await LogicArguments.RenderAsync(node, i, cancellationToken));
        return PickOne(scope, node, SplitItems(string.Join("|", rendered)))?.Trim();
    }

    private static List<string> SplitItems(string text)
    {
        List<string> items = [];
        string rest = text;
        for (
            int at = LogicArguments.StructuralIndexOf(rest, ';');
            at >= 0;
            at = LogicArguments.StructuralIndexOf(rest, ';')
        )
        {
            items.Add(rest[..at]);
            rest = rest[(at + 1)..];
        }
        items.Add(rest);
        return items;
    }

    /// <summary>Each argument is <c>weight=item</c>; the draw is proportional to the weights.</summary>
    private string? PickWeighted(IReadOnlyList<string> arguments)
    {
        List<(double Weight, string Item)> entries = [];
        foreach (string argument in arguments)
        {
            int equals = argument.IndexOf('=');
            if (
                equals < 1
                || !double.TryParse(
                    argument[..equals],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double weight
                )
                || weight < 0
            )
                return null;
            entries.Add((weight, argument[(equals + 1)..].Trim()));
        }

        double total = entries.Sum(e => e.Weight);
        if (entries.Count == 0 || total <= 0)
            return null;

        double roll = random.NextDouble() * total;
        foreach ((double weight, string item) in entries)
        {
            roll -= weight;
            if (roll < 0)
                return item;
        }
        return entries[^1].Item;
    }

    /// <summary><c>pick.n:n|a|b|c</c>: n different items in random order (n above the item count gives them all).</summary>
    private string? PickMany(IReadOnlyList<string> arguments)
    {
        if (
            arguments.Count < 2
            || !int.TryParse(
                arguments[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int count
            )
            || count < 1
        )
            return null;

        return Join(Shuffled(arguments.Skip(1).ToList()).Take(count));
    }

    private static string Join(IEnumerable<string> items) => string.Join(", ", items);

    /// <summary>Fisher-Yates over a copy.</summary>
    private List<string> Shuffled(IReadOnlyList<string> items)
    {
        List<string> copy = [.. items];
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }
}
