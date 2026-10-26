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
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Rendering;

/// <summary>
/// Renders a parsed template inside out: a token's key and arguments render first, then the evaluator turns
/// the token into text. When the evaluator returns null the token prints as written: its source text when
/// nothing inside it rendered, else <c>{</c> + its rendered content + <c>}</c> (so <c>{{user}}</c> prints
/// <c>{ViewerOne}</c>, as before the parser).
/// </summary>
public static class TemplateTreeRenderer
{
    public static async ValueTask<string> RenderAsync(
        IReadOnlyList<TemplateNode> nodes,
        Func<RenderedToken, ValueTask<string?>> evaluate
    )
    {
        StringBuilder builder = new();
        foreach (TemplateNode node in nodes)
        {
            if (node is not PlaceholderNode placeholder)
            {
                builder.Append(((TextNode)node).Text);
                continue;
            }

            RenderedToken token = placeholder.IsStatic
                ? RenderedToken.FromStatic(placeholder)
                : new RenderedToken(
                    await RenderAsync(placeholder.Key, evaluate),
                    placeholder.Arguments is null
                        ? null
                        : await RenderArgumentsAsync(placeholder.Arguments, evaluate),
                    placeholder.Raw
                );
            builder.Append(await evaluate(token) ?? AsWritten(placeholder, token));
        }
        return builder.ToString();
    }

    public static string Render(
        IReadOnlyList<TemplateNode> nodes,
        Func<RenderedToken, string?> evaluate
    )
    {
        StringBuilder builder = new();
        foreach (TemplateNode node in nodes)
        {
            if (node is not PlaceholderNode placeholder)
            {
                builder.Append(((TextNode)node).Text);
                continue;
            }

            RenderedToken token = placeholder.IsStatic
                ? RenderedToken.FromStatic(placeholder)
                : new RenderedToken(
                    Render(placeholder.Key, evaluate),
                    placeholder.Arguments?.Select(a => Render(a, evaluate)).ToList(),
                    placeholder.Raw
                );
            builder.Append(evaluate(token) ?? AsWritten(placeholder, token));
        }
        return builder.ToString();
    }

    private static async ValueTask<List<string>> RenderArgumentsAsync(
        IReadOnlyList<IReadOnlyList<TemplateNode>> arguments,
        Func<RenderedToken, ValueTask<string?>> evaluate
    )
    {
        List<string> rendered = [];
        foreach (IReadOnlyList<TemplateNode> argument in arguments)
            rendered.Add(await RenderAsync(argument, evaluate));
        return rendered;
    }

    private static string AsWritten(PlaceholderNode placeholder, RenderedToken token) =>
        placeholder.IsStatic ? placeholder.Raw : "{" + token.Content + "}";
}
