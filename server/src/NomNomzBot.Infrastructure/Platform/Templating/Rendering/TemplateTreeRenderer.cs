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
    /// <summary>
    /// Renders the tree inside out. A token whose key <paramref name="isLazy"/> accepts keeps its arguments
    /// as written and gets <see cref="RenderedToken.RenderArgument"/> to render one on demand.
    /// </summary>
    public static async ValueTask<string> RenderAsync(
        IReadOnlyList<TemplateNode> nodes,
        Func<RenderedToken, ValueTask<string?>> evaluate,
        Func<string, bool>? isLazy = null
    )
    {
        isLazy ??= _ => false;
        StringBuilder builder = new();
        foreach (TemplateNode node in nodes)
        {
            if (node is not PlaceholderNode placeholder)
            {
                builder.Append(((TextNode)node).Text);
                continue;
            }

            RenderedToken token = await TokenOfAsync(placeholder, evaluate, isLazy);
            builder.Append(await evaluate(token) ?? AsWritten(placeholder, token));
        }
        return builder.ToString();
    }

    private static async ValueTask<RenderedToken> TokenOfAsync(
        PlaceholderNode placeholder,
        Func<RenderedToken, ValueTask<string?>> evaluate,
        Func<string, bool> isLazy
    )
    {
        if (placeholder.IsStatic)
            return RenderedToken.FromStatic(placeholder, isLazy);

        string key = await RenderAsync(placeholder.Key, evaluate, isLazy);
        if (placeholder.Arguments is null)
            return new RenderedToken(key, null, placeholder.Raw);

        if (!isLazy(key.Trim()))
            return new RenderedToken(
                key,
                await RenderArgumentsAsync(placeholder.Arguments, evaluate, isLazy),
                placeholder.Raw
            );

        IReadOnlyList<IReadOnlyList<TemplateNode>> arguments = placeholder.Arguments;
        return new RenderedToken(key, RenderedToken.RawArguments(arguments), placeholder.Raw)
        {
            RenderArgument = async (index, _) =>
                await RenderAsync(arguments[index], evaluate, isLazy),
        };
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
                ? RenderedToken.FromStatic(placeholder, _ => false)
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
        Func<RenderedToken, ValueTask<string?>> evaluate,
        Func<string, bool> isLazy
    )
    {
        List<string> rendered = [];
        foreach (IReadOnlyList<TemplateNode> argument in arguments)
            rendered.Add(await RenderAsync(argument, evaluate, isLazy));
        return rendered;
    }

    private static string AsWritten(PlaceholderNode placeholder, RenderedToken token) =>
        placeholder.IsStatic ? placeholder.Raw : "{" + token.Content + "}";
}
