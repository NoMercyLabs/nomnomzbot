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
