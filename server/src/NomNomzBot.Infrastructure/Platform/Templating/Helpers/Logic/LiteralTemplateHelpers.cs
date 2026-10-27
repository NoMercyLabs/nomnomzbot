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
/// <c>{literal:text}</c> (catalog section 7.5): the text exactly as written, with nothing inside it resolved
/// (<c>{literal:{user}}</c> prints <c>{user}</c>). The renderer hands a lazy helper the source text of its
/// arguments, so a nested token is never rendered.
/// </summary>
public sealed class LiteralTemplateHelpers : LogicHelperProvider
{
    public override string Namespace => "literal";

    public override IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [Row("literal", "{user}", lazy: true)];

    public override ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) => Result(node.Arguments.Count == 0 ? null : string.Join("|", node.Arguments));
}
