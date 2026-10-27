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

/// <summary>
/// The one template tokenizer (catalog section 3). It builds a tree of <see cref="TextNode"/> and
/// <see cref="PlaceholderNode"/>: the first <c>:</c> of a token starts its arguments, <c>|</c> at the token's
/// own level splits them, and a <c>{…}</c> inside the key or an argument is a nested token. <c>\{ \} \| \: \\</c>
/// print the character. An unclosed or empty token, a stray <c>}</c>, and any token nested deeper than
/// <see cref="MaxDepth"/> stay text exactly as written.
/// </summary>
public static class TemplateParser
{
    public const int MaxDepth = 8;

    private const string Escapable = "{}|:\\";

    public static IReadOnlyList<TemplateNode> Parse(string template)
    {
        List<TemplateNode> nodes = [];
        int position = 0;
        ParseSequence(template, ref position, depth: 0, nodes, stopAtSeparators: false);
        return nodes;
    }

    /// <summary>
    /// Reads nodes until the end of the text, or — inside a token — until an unescaped <c>}</c>, or a
    /// <c>:</c>/<c>|</c> when <paramref name="stopAtSeparators"/> asks for it. The stop character is not consumed.
    /// </summary>
    private static void ParseSequence(
        string text,
        ref int position,
        int depth,
        List<TemplateNode> nodes,
        bool stopAtSeparators,
        bool stopAtColon = false
    )
    {
        StringBuilder plain = new();
        int textStart = position;

        while (position < text.Length)
        {
            char current = text[position];

            if (
                current == '\\'
                && position + 1 < text.Length
                && Escapable.Contains(text[position + 1])
            )
            {
                plain.Append(text[position + 1]);
                position += 2;
                continue;
            }

            if (
                depth > 0
                && (
                    current == '}'
                    || (stopAtSeparators && current == '|')
                    || (stopAtColon && current == ':')
                )
            )
                break;

            if (current == '{')
            {
                int tokenStart = position;
                TemplateNode? token =
                    depth + 1 > MaxDepth
                        ? ReadLiteralGroup(text, ref position)
                        : ReadPlaceholder(text, ref position, depth + 1);
                if (token is PlaceholderNode)
                {
                    Flush(text, textStart, tokenStart, plain, nodes);
                    nodes.Add(token);
                    textStart = position;
                    continue;
                }
                if (token is TextNode literal)
                {
                    plain.Append(literal.Text);
                    continue;
                }

                // Not a token: the brace is plain text and parsing goes on right after it.
                position = tokenStart + 1;
                plain.Append('{');
                continue;
            }

            plain.Append(current);
            position++;
        }

        Flush(text, textStart, position, plain, nodes);
    }

    /// <summary>Reads one token starting at its <c>{</c>; null (position unchanged) when it never closes or is empty.</summary>
    private static PlaceholderNode? ReadPlaceholder(string text, ref int position, int depth)
    {
        int start = position;
        int cursor = position + 1;

        List<TemplateNode> key = [];
        ParseSequence(text, ref cursor, depth, key, stopAtSeparators: false, stopAtColon: true);

        List<IReadOnlyList<TemplateNode>>? arguments = null;
        if (cursor < text.Length && text[cursor] == ':')
        {
            arguments = [];
            do
            {
                cursor++;
                List<TemplateNode> argument = [];
                ParseSequence(text, ref cursor, depth, argument, stopAtSeparators: true);
                arguments.Add(argument);
            } while (cursor < text.Length && text[cursor] == '|');
        }

        if (cursor >= text.Length || text[cursor] != '}')
            return null;
        if (key.Count == 0 && arguments is null)
            return null;

        cursor++;
        position = cursor;
        return new PlaceholderNode(key, arguments, text[start..cursor]);
    }

    /// <summary>
    /// Past <see cref="MaxDepth"/>: the whole balanced <c>{…}</c> group is kept as text, exactly as written.
    /// Null (position unchanged) when the group never closes.
    /// </summary>
    private static TextNode? ReadLiteralGroup(string text, ref int position)
    {
        int level = 0;
        for (int cursor = position; cursor < text.Length; cursor++)
        {
            char current = text[cursor];
            if (current == '\\' && cursor + 1 < text.Length && Escapable.Contains(text[cursor + 1]))
            {
                cursor++;
                continue;
            }
            if (current == '{')
                level++;
            else if (current == '}' && --level == 0)
            {
                string raw = text[position..(cursor + 1)];
                position = cursor + 1;
                return new TextNode(raw, raw);
            }
        }
        return null;
    }

    private static void Flush(
        string text,
        int start,
        int end,
        StringBuilder plain,
        List<TemplateNode> nodes
    )
    {
        if (end <= start)
            return;
        nodes.Add(new TextNode(plain.ToString(), text[start..end]));
        plain.Clear();
    }
}
