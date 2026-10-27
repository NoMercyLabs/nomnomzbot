// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Parsing;

/// <summary>Walks a parsed template: finds its innermost tokens, and writes it back with some tokens replaced.</summary>
public static class TemplateTree
{
    /// <summary>
    /// Every token with no token inside it, anywhere in the tree (in a key, an argument, or on top). The
    /// arguments of a token whose static key <paramref name="isLazy"/> accepts are not searched: they resolve
    /// only if the helper asks for them.
    /// </summary>
    public static IEnumerable<PlaceholderNode> StaticPlaceholders(
        IReadOnlyList<TemplateNode> nodes,
        Func<string, bool>? isLazy = null
    )
    {
        foreach (PlaceholderNode node in nodes.OfType<PlaceholderNode>())
        {
            if (node.IsStatic)
            {
                yield return node;
                continue;
            }

            foreach (PlaceholderNode inner in StaticPlaceholders(node.Key, isLazy))
                yield return inner;

            if (HasLazyArguments(node, isLazy))
                continue;

            foreach (IReadOnlyList<TemplateNode> argument in node.Arguments ?? [])
            foreach (PlaceholderNode inner in StaticPlaceholders(argument, isLazy))
                yield return inner;
        }
    }

    private static bool HasLazyArguments(PlaceholderNode node, Func<string, bool>? isLazy) =>
        isLazy is not null
        && node.Arguments is not null
        && node.Key.All(n => n is TextNode)
        && isLazy(PlaceholderNode.TextOf(node.Key).Trim());

    /// <summary>
    /// Writes the template back as source text, with each innermost token for which
    /// <paramref name="replace"/> returns text replaced by that text. Everything else, escapes included,
    /// stays exactly as written, so the result parses again the same way.
    /// </summary>
    public static async Task<string> RewriteAsync(
        IReadOnlyList<TemplateNode> nodes,
        Func<PlaceholderNode, Task<string?>> replace
    )
    {
        StringBuilder builder = new();
        await AppendAsync(builder, nodes, replace);
        return builder.ToString();
    }

    private static async Task AppendAsync(
        StringBuilder builder,
        IReadOnlyList<TemplateNode> nodes,
        Func<PlaceholderNode, Task<string?>> replace
    )
    {
        foreach (TemplateNode node in nodes)
        {
            if (node is not PlaceholderNode placeholder)
            {
                builder.Append(node.Raw);
                continue;
            }

            if (placeholder.IsStatic)
            {
                builder.Append(await replace(placeholder) ?? placeholder.Raw);
                continue;
            }

            builder.Append('{');
            await AppendAsync(builder, placeholder.Key, replace);
            if (placeholder.Arguments is not null)
            {
                for (int index = 0; index < placeholder.Arguments.Count; index++)
                {
                    builder.Append(index == 0 ? ':' : '|');
                    await AppendAsync(builder, placeholder.Arguments[index], replace);
                }
            }
            builder.Append('}');
        }
    }
}
