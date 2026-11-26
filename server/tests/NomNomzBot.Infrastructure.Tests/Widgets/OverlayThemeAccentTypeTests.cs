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

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Small accent type must read on every theme, whatever accent the streamer picks: each theme names its own
/// accent-text shade, and type on an accent tile takes the ink the overlay picks for that accent, never a fixed white.
/// </summary>
public sealed partial class OverlayThemeAccentTypeTests
{
    private static readonly string[] Themes = ["sticker", "ink", "classic", "print"];

    [GeneratedRegex(
        @"\[data-nnz-theme='(?<theme>[a-z]+)'\]\s*\{(?<body>[^}]*)\}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex ThemeBlock();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CssComment();

    private static Dictionary<string, Dictionary<string, string>> ThemeTokens()
    {
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(
                "NomNomzBot.Infrastructure.Widgets.Theme.overlay-theme.css"
            );
        stream.Should().NotBeNull("the overlay theme ships as an embedded stylesheet");
        using StreamReader reader = new(stream);
        string css = CssComment().Replace(reader.ReadToEnd(), "");

        Dictionary<string, Dictionary<string, string>> themes = [];
        foreach (Match block in ThemeBlock().Matches(css))
        {
            Dictionary<string, string> tokens = [];
            foreach (string line in block.Groups["body"].Value.Split(';'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                    tokens[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
            themes[block.Groups["theme"].Value] = tokens;
        }
        return themes;
    }

    [Fact]
    public void Every_theme_names_the_accent_shade_its_small_type_uses()
    {
        Dictionary<string, Dictionary<string, string>> themes = ThemeTokens();

        foreach (string theme in Themes)
            themes[theme]
                .Should()
                .ContainKey(
                    "--nnz-accent-text",
                    $"{theme} must not inherit the sticker shade from :root"
                );
    }

    [Fact]
    public void Type_on_an_accent_tile_takes_the_ink_picked_for_that_accent()
    {
        Dictionary<string, Dictionary<string, string>> themes = ThemeTokens();

        foreach ((string theme, Dictionary<string, string> tokens) in themes)
        foreach (
            (string tile, string type) in new[]
            {
                ("--nnz-chip-bg", "--nnz-chip-text"),
                ("--nnz-num-bg", "--nnz-num-text"),
            }
        )
            if (tokens.GetValueOrDefault(tile) == "var(--nnz-accent)")
                tokens[type]
                    .Should()
                    .Be("var(--nnz-on-accent)", $"{theme} draws {type} on the accent");
    }
}
