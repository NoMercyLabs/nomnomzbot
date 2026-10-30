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
/// <c>{chance:percent|then[|else]}</c> (catalog section 7.5): the then branch with the given chance (0 to 100),
/// else the else branch or nothing. Only the branch the draw chooses renders. The public constructor draws from
/// <see cref="Random.Shared"/>; a test hands the internal one a seeded <see cref="Random"/>.
/// </summary>
public sealed class ChanceTemplateHelpers(Random random) : LogicHelperProvider
{
    public ChanceTemplateHelpers()
        : this(Random.Shared) { }

    public override string Namespace => "chance";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [Row("chance", "no luck", lazy: true)];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count is not (2 or 3))
            return null;

        string percentText = await LogicArguments.RenderParsedAsync(node, 0, cancellationToken);
        if (!LogicArguments.TryNumber(percentText, out double percent) || percent is < 0 or > 100)
            return null;

        bool hit = random.NextDouble() * 100 < percent;
        return await LogicArguments.RenderOrEmptyAsync(node, hit ? 1 : 2, cancellationToken);
    }
}
