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
/// <c>{default:value|fallback}</c> (catalog section 7.5): the value when it is not blank, else the fallback.
/// The fallback renders only when it is used.
/// </summary>
public sealed class DefaultTemplateHelpers : LogicHelperProvider
{
    public override string Namespace => "default";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [Row("default", "Software and Game Development", lazy: true)];

    public override async ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    )
    {
        if (node.Arguments.Count != 2)
            return null;

        string value = await LogicArguments.RenderAsync(node, 0, cancellationToken);
        return value.Length > 0
            ? value
            : await LogicArguments.RenderAsync(node, 1, cancellationToken);
    }
}
