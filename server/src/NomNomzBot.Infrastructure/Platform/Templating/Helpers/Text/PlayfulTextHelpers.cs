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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Chat-style text effects: leet, rot13, uwu, clap, stutter, yell, and the chat-safe escape that stops a
/// leading <c>/</c> or <c>.</c> from running a chat command.
/// </summary>
internal sealed class PlayfulTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText("text.leet", "l337", Leet),
        TextHelperRow.OfText("text.rot13", "uryyb", Rot13),
        TextHelperRow.OfText("text.uwu", "weawwy", Uwu),
        new TextHelperRow("text.clap", "big 👏 world", Clap),
        TextHelperRow.OfText("text.stutter", "h-hello", Stutter),
        TextHelperRow.OfText("text.yell", "HELLO!", text => text.ToUpperInvariant() + "!"),
        TextHelperRow.OfText("text.escape.chat", "⁄ban x", EscapeChat)
    )
{
    private const string DefaultClap = "👏";

    /// <summary>U+2044 FRACTION SLASH looks like a slash and is not a command prefix.</summary>
    private const char LookalikeSlash = '⁄';

    /// <summary>U+2024 ONE DOT LEADER looks like a dot and is not a command prefix.</summary>
    private const char LookalikeDot = '․';

    private static string Leet(string text) =>
        string.Concat(
            text.Select(c =>
                char.ToLowerInvariant(c) switch
                {
                    'a' => '4',
                    'e' => '3',
                    'i' => '1',
                    'o' => '0',
                    's' => '5',
                    't' => '7',
                    _ => c,
                }
            )
        );

    private static string Rot13(string text) =>
        string.Concat(
            text.Select(c =>
                c switch
                {
                    >= 'a' and <= 'z' => (char)('a' + ((c - 'a' + 13) % 26)),
                    >= 'A' and <= 'Z' => (char)('A' + ((c - 'A' + 13) % 26)),
                    _ => c,
                }
            )
        );

    private static string Uwu(string text) =>
        string.Concat(
            text.Select(c =>
                c switch
                {
                    'r' or 'l' => 'w',
                    'R' or 'L' => 'W',
                    _ => c,
                }
            )
        );

    /// <summary><c>{text.clap:t[|sep]}</c>: the words of t with a clap (or sep) between them.</summary>
    private static string? Clap(IReadOnlyList<string> args)
    {
        if (args.Count is < 1 or > 2)
            return null;

        return string.Join(
            " " + TextArguments.TextAt(args, 1, DefaultClap) + " ",
            TextArguments.Words(args[0])
        );
    }

    private static string Stutter(string text)
    {
        List<string> elements = TextArguments.Elements(text);
        return elements.Count == 0 ? string.Empty : elements[0] + "-" + text;
    }

    private static string EscapeChat(string text) =>
        text.Length == 0 ? text
        : text[0] == '/' ? LookalikeSlash + text[1..]
        : text[0] == '.' ? LookalikeDot + text[1..]
        : text;
}
