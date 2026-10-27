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

        string percentText = await LogicArguments.RenderAsync(node, 0, cancellationToken);
        if (!LogicArguments.TryNumber(percentText, out double percent) || percent is < 0 or > 100)
            return null;

        bool hit = random.NextDouble() * 100 < percent;
        return await LogicArguments.RenderOrEmptyAsync(node, hit ? 1 : 2, cancellationToken);
    }
}
