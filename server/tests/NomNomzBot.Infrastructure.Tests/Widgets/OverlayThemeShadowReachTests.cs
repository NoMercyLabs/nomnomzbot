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

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A widget pinned to a browser-source edge sat 4 px from it, but the sticker and print shadows reach 6 to 10 px
/// past the box, so OBS cut them off. Each theme now names how far its shadow reaches, and the pin keeps that room.
/// </summary>
public sealed partial class OverlayThemeShadowReachTests
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

    // A bare 0 is a length too ("0 10px 30px" is offset 0, offset 10, blur 30); colors are stripped first.
    [GeneratedRegex(
        @"(?<![\w.])-?\d+(?:\.\d+)?(?:px)?(?![\w.%])",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Length();

    [GeneratedRegex(
        @"#[0-9a-fA-F]+|[a-z-]+\([^)]*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Color();

    private static string Css()
    {
        using System.IO.Stream? stream = typeof(FirstPartyWidgetCatalogue)
            .GetTypeInfo()
            .Assembly.GetManifestResourceStream(
                "NomNomzBot.Infrastructure.Widgets.Theme.overlay-theme.css"
            );
        stream.Should().NotBeNull("the overlay theme ships as an embedded stylesheet");
        using StreamReader reader = new(stream);
        return CssComment().Replace(reader.ReadToEnd(), "");
    }

    private static Dictionary<string, Dictionary<string, string>> ThemeTokens(string css)
    {
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

    private static double Px(string length) =>
        double.Parse(length.Replace("px", ""), CultureInfo.InvariantCulture);

    /// <summary>The farthest a hard shadow paints past the box: the larger offset plus the spread (blur is soft and left out).</summary>
    private static double ShadowReach(string shadow)
    {
        if (shadow == "none")
            return 0;
        double reach = 0;
        foreach (string layer in Color().Replace(shadow, "").Split(','))
        {
            List<double> lengths = Length().Matches(layer).Select(m => Px(m.Value)).ToList();
            if (lengths.Count < 2)
                continue;
            double spread = lengths.Count >= 4 ? lengths[3] : 0;
            reach = Math.Max(reach, Math.Max(Math.Abs(lengths[0]), Math.Abs(lengths[1])) + spread);
        }
        return reach;
    }

    [Fact]
    public void Every_theme_keeps_room_for_the_full_reach_of_its_shadow()
    {
        Dictionary<string, Dictionary<string, string>> themes = ThemeTokens(Css());

        foreach (string theme in Themes)
        {
            themes[theme]
                .Should()
                .ContainKey("--nnz-shadow-reach", $"{theme} must say how far its shadow reaches");
            double reach = Px(themes[theme]["--nnz-shadow-reach"]);
            reach
                .Should()
                .BeGreaterThanOrEqualTo(
                    ShadowReach(themes[theme]["--nnz-shadow"]),
                    $"the {theme} shadow must not be cut off at a browser-source edge"
                );
        }
    }

    [Fact]
    public void A_pinned_widget_sits_its_edge_offset_plus_the_shadow_reach_from_the_edge()
    {
        Css()
            .Should()
            .Contain(
                "--nnz-inset: calc(var(--nnz-edge) / var(--nnz-own-zoom) + var(--nnz-shadow-reach));"
            );
    }
}
