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
