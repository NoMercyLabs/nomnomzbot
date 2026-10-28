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

using System.Globalization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The pure random rows of the <c>random</c> namespace (catalog section 7.5): <c>random.number</c>,
/// <c>random.decimal</c>, <c>random.percent</c>, <c>random.bool</c>, <c>random.letter</c> and
/// <c>random.color</c>. The provider holds only these keys. The as-built keys (<c>random.number.100</c>,
/// <c>random.pick.a.b</c>, <c>random.user</c>) still resolve through the as-built helpers, and the chatter and
/// quote rows belong to other lanes. The public constructor draws from <see cref="Random.Shared"/>; a test hands
/// the internal one a seeded <see cref="Random"/>.
/// </summary>
public sealed class RandomTemplateHelpers(Random random) : LogicHelperProvider
{
    private const int MaxDecimalPlaces = 8;

    public RandomTemplateHelpers()
        : this(Random.Shared) { }

    public override string Namespace => "random";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        // The as-built string template.helper.random_number already holds the dotted form's help.
        Row("random.number", "4", descriptionKey: "template.helper.random.number.draw"),
        Row("random.decimal", "0.42"),
        Row("random.percent", "37%"),
        Row("random.bool", "true"),
        Row("random.letter", "K"),
        Row("random.color", "#3FA7D6"),
    ];

    public override ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        Result(
            node.Key.ToLowerInvariant() switch
            {
                "random.number" => Number(node.Arguments),
                "random.decimal" => Decimal(node.Arguments),
                "random.percent" => Plain(
                    node,
                    random.Next(0, 101).ToString(CultureInfo.InvariantCulture) + "%"
                ),
                "random.bool" => Plain(node, random.Next(2) == 1 ? "true" : "false"),
                "random.letter" => Plain(node, ((char)('A' + random.Next(26))).ToString()),
                "random.color" => Plain(
                    node,
                    "#" + random.Next(0x1000000).ToString("X6", CultureInfo.InvariantCulture)
                ),
                _ => null,
            }
        );

    /// <summary>A row with no arguments: a stray argument is a mistake, so the token prints as written.</summary>
    private static string? Plain(HelperNode node, string value) =>
        node.Arguments.Count == 0 ? value : null;

    /// <summary><c>number:max</c> draws 1 to max; <c>number:min|max[|step]</c> draws on the step (same spec as the dotted form).</summary>
    private string? Number(IReadOnlyList<string> arguments)
    {
        string[] parts = arguments.Count == 1 ? ["1", arguments[0]] : [.. arguments];
        return RandomNumberSpec.TryParse(parts, out RandomNumberSpec spec)
            ? spec.Draw(random).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary><c>decimal:min|max|places</c>: a number from min to max, written with the given decimals.</summary>
    private string? Decimal(IReadOnlyList<string> arguments)
    {
        if (
            arguments.Count != 3
            || !LogicArguments.TryNumber(arguments[0], out double min)
            || !LogicArguments.TryNumber(arguments[1], out double max)
            || !int.TryParse(
                arguments[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int places
            )
            || max < min
            || places > MaxDecimalPlaces
        )
            return null;

        double value = min + (random.NextDouble() * (max - min));
        return value.ToString("F" + places, CultureInfo.InvariantCulture);
    }
}
