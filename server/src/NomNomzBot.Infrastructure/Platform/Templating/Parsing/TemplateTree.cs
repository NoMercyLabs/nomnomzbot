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
    /// <summary>Every token with no token inside it, anywhere in the tree (in a key, an argument, or on top).</summary>
    public static IEnumerable<PlaceholderNode> StaticPlaceholders(IReadOnlyList<TemplateNode> nodes)
    {
        foreach (PlaceholderNode node in nodes.OfType<PlaceholderNode>())
        {
            if (node.IsStatic)
            {
                yield return node;
                continue;
            }

            foreach (PlaceholderNode inner in StaticPlaceholders(node.Key))
                yield return inner;
            foreach (IReadOnlyList<TemplateNode> argument in node.Arguments ?? [])
            foreach (PlaceholderNode inner in StaticPlaceholders(argument))
                yield return inner;
        }
    }

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
