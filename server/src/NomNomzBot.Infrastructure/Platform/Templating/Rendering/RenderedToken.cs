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
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Rendering;

/// <summary>
/// One token after its key and arguments rendered (inside out), as written: nothing trimmed.
/// <see cref="Content"/> is the token text between the braces; <see cref="Raw"/> the source text.
/// </summary>
public sealed record RenderedToken(string Key, IReadOnlyList<string>? Arguments, string Raw)
{
    public string Content => Arguments is null ? Key : Key + ":" + string.Join("|", Arguments);

    /// <summary>The provider's view: key and arguments trimmed (catalog section 3, Quoting).</summary>
    public HelperNode ToHelperNode() =>
        new(Key.Trim(), [.. (Arguments ?? []).Select(a => a.Trim())], Raw);

    /// <summary>A token with no token inside: its text is known before any render.</summary>
    public static RenderedToken FromStatic(PlaceholderNode node) =>
        new(
            PlaceholderNode.TextOf(node.Key),
            node.Arguments is null ? null : [.. node.Arguments.Select(PlaceholderNode.TextOf)],
            node.Raw
        );
}
