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
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Rendering;

/// <summary>
/// One token after its key and arguments rendered (inside out), as written: nothing trimmed.
/// <see cref="Content"/> is the token text between the braces; <see cref="Raw"/> the source text.
/// </summary>
public sealed record RenderedToken(string Key, IReadOnlyList<string>? Arguments, string Raw)
{
    /// <summary>
    /// Renders argument <c>i</c> on demand; set when the token's entry takes lazy arguments, and then
    /// <see cref="Arguments"/> holds the raw argument text.
    /// </summary>
    public Func<int, CancellationToken, ValueTask<string>>? RenderArgument { get; init; }

    public string Content => Arguments is null ? Key : Key + ":" + string.Join("|", Arguments);

    /// <summary>
    /// The provider's view: the key trimmed, every argument as written (catalog section 3, Quoting). A helper
    /// that parses an argument ignores the spaces around it; a helper that prints one keeps them.
    /// </summary>
    public HelperNode ToHelperNode() =>
        new(Key.Trim(), [.. Arguments ?? []], Raw) { RenderArgumentAsync = RenderArgument };

    /// <summary>
    /// A token with no token inside: its text is known before any render. The arguments of a lazy key
    /// (<paramref name="isLazy"/>) stay as written.
    /// </summary>
    public static RenderedToken FromStatic(PlaceholderNode node, Func<string, bool> isLazy)
    {
        string key = PlaceholderNode.TextOf(node.Key);
        if (node.Arguments is null)
            return new RenderedToken(key, null, node.Raw);

        return new RenderedToken(
            key,
            isLazy(key.Trim())
                ? RawArguments(node.Arguments)
                : [.. node.Arguments.Select(PlaceholderNode.TextOf)],
            node.Raw
        );
    }

    /// <summary>The source text of each argument: nested tokens and escapes as written.</summary>
    public static List<string> RawArguments(IReadOnlyList<IReadOnlyList<TemplateNode>> arguments) =>
        [.. arguments.Select(argument => string.Concat(argument.Select(n => n.Raw)))];
}
