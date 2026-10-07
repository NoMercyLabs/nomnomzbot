// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The first-party widgets are the code a streamer opens and copies, so they must show the typed way to write a
/// widget: no TypeScript <c>any</c> in a <c>&lt;script&gt;</c> block.
/// </summary>
public sealed class FirstPartyWidgetTypedScriptTests
{
    private const string AssetPrefix = "NomNomzBot.Infrastructure.Content.Widgets.Assets.";

    /// <summary>
    /// Every shipped first-party widget source. Read from the embedded assets; the seeder tests prove each one
    /// has a catalogue entry.
    /// </summary>
    private static IReadOnlyList<string> AssetKeys { get; } =
    [
        .. typeof(FirstPartyWidgetCatalogueSeeder)
            .Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(AssetPrefix) && name.EndsWith(".vue"))
            .Select(name => name[AssetPrefix.Length..^".vue".Length])
            .Order(StringComparer.Ordinal),
    ];

    public static TheoryData<string> AssetSources()
    {
        TheoryData<string> keys = [];
        foreach (string key in AssetKeys)
            keys.Add(key);
        return keys;
    }

    [Theory]
    [MemberData(nameof(AssetSources))]
    public void A_first_party_widget_has_no_any_in_its_script(string key)
    {
        IReadOnlyList<int> lines = ScriptAnyLines(LoadAsset(key));

        lines
            .Should()
            .BeEmpty(
                $"{key}.vue must be typed: it has an `any` on line(s) {string.Join(", ", lines)}"
            );
    }

    [Fact]
    public void Every_catalogue_widget_has_a_source_in_the_scan()
    {
        FirstPartyWidgetCatalogue
            .All.Select(definition => definition.Key)
            .Except(AssetKeys)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void The_scanner_finds_every_form_of_any_and_the_line_it_is_on()
    {
        string source = string.Join(
            "\n",
            "<script setup lang=\"ts\">",
            "const a = (window as any).X",
            "function f(p: any): void {}",
            "const b = <any>x",
            "const c: any[] = []",
            "const d: Array<any> = []",
            "const e: Record<string, any> = {}",
            "</script>"
        );

        ScriptAnyLines(source).Should().Equal(2, 3, 4, 5, 6, 7);
    }

    [Fact]
    public void The_scanner_ignores_any_in_comments_strings_members_and_the_template()
    {
        string source = string.Join(
            "\n",
            "<script setup lang=\"ts\">",
            "// loose type: any",
            "/* any",
            "   any */",
            "const s: string = 'any'",
            "const t: string = \"any\"",
            "const u: string = `any ${'x'}`",
            "const v: boolean = list.some(Boolean) || company.any",
            "const w: number = many + anyone",
            "</script>",
            "<template><p>any</p></template>",
            "<style>.any { color: red }</style>"
        );

        ScriptAnyLines(source).Should().BeEmpty();
    }

    private static string LoadAsset(string key)
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        string resourceName = $"{AssetPrefix}{key}.vue";
        using System.IO.Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded asset '{resourceName}' is missing."
            );
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static readonly Regex ScriptBlock = new(
        @"<script\b[^>]*>(?<body>.*?)</script>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant
    );

    private static readonly Regex AnyKeyword = new(
        @"(?<![\w$.])any(?![\w$])",
        RegexOptions.CultureInvariant
    );

    /// <summary>
    /// The 1-based lines of the file where a <c>&lt;script&gt;</c> block uses the TypeScript <c>any</c> keyword.
    /// Everything outside a script block, and every comment and string inside one, is blanked first so the
    /// line numbers stay those of the file.
    /// </summary>
    private static IReadOnlyList<int> ScriptAnyLines(string source)
    {
        char[] scripts = [.. source.Select(c => c == '\n' ? '\n' : ' ')];
        foreach (Match block in ScriptBlock.Matches(source))
        {
            Group body = block.Groups["body"];
            source.CopyTo(body.Index, scripts, body.Index, body.Length);
        }

        string code = BlankCommentsAndStrings(new string(scripts));
        List<int> lines = [];
        foreach (Match match in AnyKeyword.Matches(code))
            lines.Add(code.Take(match.Index).Count(c => c == '\n') + 1);
        return lines;
    }

    private static string BlankCommentsAndStrings(string code)
    {
        StringBuilder result = new(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            char next = i + 1 < code.Length ? code[i + 1] : '\0';
            int end;
            if (c == '/' && next == '/')
                end = IndexOrEnd(code, "\n", i);
            else if (c == '/' && next == '*')
                end = Math.Min(code.Length, IndexOrEnd(code, "*/", i + 2) + 2);
            else if (c is '\'' or '"' or '`')
                end = EndOfString(code, i);
            else
            {
                result.Append(c);
                i++;
                continue;
            }

            for (int k = i; k < end; k++)
                result.Append(code[k] == '\n' ? '\n' : ' ');
            i = end;
        }
        return result.ToString();
    }

    private static int IndexOrEnd(string code, string token, int from)
    {
        int index = code.IndexOf(token, from, StringComparison.Ordinal);
        return index < 0 ? code.Length : index;
    }

    // The index just past the closing quote; a backslash skips the next character.
    private static int EndOfString(string code, int open)
    {
        char quote = code[open];
        int i = open + 1;
        while (i < code.Length && code[i] != quote)
            i += code[i] == '\\' ? 2 : 1;
        return Math.Min(code.Length, i + 1);
    }
}
