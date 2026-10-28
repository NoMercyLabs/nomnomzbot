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
/// <c>{coalesce:a|b|c}</c> (catalog section 7.5): the first argument that is not blank, or nothing when all are.
/// An argument after the first non-blank one never renders.
/// </summary>
public sealed class CoalesceTemplateHelpers : LogicHelperProvider
{
    public override string Namespace => "coalesce";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [Row("coalesce", "ViewerTwo", lazy: true)];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        for (int i = 0; i < node.Arguments.Count; i++)
        {
            string value = await LogicArguments.RenderAsync(node, i, cancellationToken);
            if (value.Length > 0)
                return value;
        }
        return "";
    }
}
