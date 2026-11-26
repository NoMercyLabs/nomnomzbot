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
using Jint;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The overlay theme color setting changes the one main color variable of the look it is on, and the layout
/// settings reach the two layout variables. The script under test is the real one the overlay SDK serves, run
/// against a recording stand-in for the page root.
/// </summary>
public sealed class OverlaySdkThemeColorTests
{
    private const string StartMarker = "var THEMES = ";
    private const string EndMarker = "applyTheme(currentSettings);";

    private static string ServedSdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        return controller.Get().Should().BeOfType<ContentResult>().Subject.Content ?? string.Empty;
    }

    /// <summary>The theme block of the SDK (tables and functions) run against a root that records its style.</summary>
    private static Engine ThemeEngine()
    {
        string sdk = ServedSdk();
        int start = sdk.IndexOf(StartMarker, StringComparison.Ordinal);
        int end = sdk.IndexOf(EndMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the SDK holds the theme block");
        end.Should().BeGreaterThan(start);

        Engine engine = new();
        engine.Execute(
            """
            var styles = {};
            var attrs = {};
            var params = { get: function () { return null; } };
            var document = { documentElement: {
              setAttribute: function (k, v) { attrs[k] = v; },
              style: {
                setProperty: function (k, v) { styles[k] = v; },
                removeProperty: function (k) { delete styles[k]; }
              }
            } };
            """
        );
        engine.Execute(sdk[start..end]);
        return engine;
    }

    private static Dictionary<string, string> Applied(Engine engine, string settingsJson)
    {
        engine.Execute("styles = {}; applyTheme(" + settingsJson + ");");
        return engine
            .Evaluate("Object.keys(styles).map(function (k) { return k + '=' + styles[k]; })")
            .AsArray()
            .Select(v => v.AsString().Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);
    }

    [Theory]
    [InlineData("sticker", "--nnz-accent")]
    [InlineData("ink", "--nnz-accent")]
    [InlineData("print", "--nnz-surface")]
    [InlineData("classic", "--nnz-accent")]
    public void A_chosen_color_sets_the_main_variable_of_the_look_it_is_on_and_no_other(
        string theme,
        string mainVariable
    )
    {
        Engine engine = ThemeEngine();

        Dictionary<string, string> styles = Applied(
            engine,
            $"{{ overlayTheme: '{theme}', accentColor: '#123abc' }}"
        );

        styles[mainVariable].Should().Be("#123abc");
        styles.Where(kv => kv.Value == "#123abc").Select(kv => kv.Key).Should().Equal(mainVariable);
    }

    // Owner report 2026-11-25: a dark chosen color on a print overlay kept dark text, so nothing read.
    [Theory]
    [InlineData("print", "#000000", "--nnz-text", "#ffffff")]
    [InlineData("print", "#ffff00", "--nnz-text", "#121212")]
    [InlineData("sticker", "#000000", "--nnz-on-accent", "#ffffff")]
    [InlineData("sticker", "#ffff00", "--nnz-on-accent", "#121212")]
    public void Text_on_a_chosen_color_is_black_or_white_for_contrast(
        string theme,
        string color,
        string textVariable,
        string ink
    )
    {
        Dictionary<string, string> styles = Applied(
            ThemeEngine(),
            $"{{ overlayTheme: '{theme}', accentColor: '{color}' }}"
        );

        styles[textVariable].Should().Be(ink);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reset")]
    [InlineData("#9146ff")]
    public void An_empty_reset_or_legacy_color_leaves_the_look_its_own_color(string color)
    {
        Dictionary<string, string> styles = Applied(
            ThemeEngine(),
            $"{{ overlayTheme: 'print', accentColor: '{color}' }}"
        );

        styles.Keys.Should().NotContain(["--nnz-accent", "--nnz-surface"]);
    }

    [Theory]
    [InlineData("sticker", "--nnz-accent")]
    [InlineData("print", "--nnz-surface")]
    public void The_global_color_replaces_the_look_color_when_the_widget_has_no_color_of_its_own(
        string theme,
        string mainVariable
    )
    {
        Engine engine = ThemeEngine();

        // Empty, "reset", the look's own main color and a pre-themes default all mean "no color of its own".
        foreach (string own in new[] { "", "reset", "#9146ff", "#ff2d6f" })
            Applied(
                engine,
                $"{{ overlayTheme: '{theme}', accentColor: '{own}', globalAccentColor: '#0a1b2c' }}"
            )[mainVariable]
                .Should()
                .Be("#0a1b2c", $"own color '{own}'");
    }

    [Fact]
    public void The_widgets_own_color_beats_the_global_color()
    {
        Dictionary<string, string> styles = Applied(
            ThemeEngine(),
            "{ overlayTheme: 'sticker', accentColor: '#123abc', globalAccentColor: '#0a1b2c' }"
        );

        styles["--nnz-accent"].Should().Be("#123abc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-color")]
    [InlineData("red")]
    public void An_empty_or_invalid_global_color_leaves_the_look_its_own_color(string global)
    {
        Dictionary<string, string> styles = Applied(
            ThemeEngine(),
            $"{{ overlayTheme: 'sticker', accentColor: '', globalAccentColor: '{global}' }}"
        );

        styles.Keys.Should().NotContain(["--nnz-accent", "--nnz-surface"]);
    }

    [Fact]
    public void The_main_color_of_every_look_counts_as_untouched_so_the_default_does_not_override_it()
    {
        Engine engine = ThemeEngine();

        foreach ((string theme, string main) in FirstPartyWidgetCatalogue.ThemeMainColors)
            Applied(engine, $"{{ overlayTheme: '{theme}', accentColor: '{main}' }}")
                .Keys.Should()
                .NotContain(["--nnz-accent", "--nnz-surface"], theme);
    }

    [Fact]
    public void The_script_tables_equal_the_catalogue_tables_and_the_setting_default_is_the_default_look_main_color()
    {
        Engine engine = ThemeEngine();

        foreach ((string theme, string main) in FirstPartyWidgetCatalogue.ThemeMainColors)
            engine.Evaluate($"MAIN_COLOR['{theme}']").AsString().Should().Be(main, theme);
        engine
            .Evaluate("THEMES.join(',')")
            .AsString()
            .Should()
            .Be(string.Join(',', FirstPartyWidgetCatalogue.Themes));

        List<WidgetSettingsField> colors =
        [
            .. new WidgetSettingsSchemaProvider()
                .GetAll()
                .Where(s => s.Fields.Any(f => f.Key == FirstPartyWidgetCatalogue.ThemeSettingKey))
                .SelectMany(s => s.Fields)
                .Where(f => f.Key == FirstPartyWidgetCatalogue.AccentSettingKey),
        ];
        colors.Should().NotBeEmpty("widgets offer the color setting");
        colors
            .Should()
            .OnlyContain(f =>
                (string?)f.Default
                == FirstPartyWidgetCatalogue.ThemeMainColors[FirstPartyWidgetCatalogue.Themes[0]]
            );
    }

    [Fact]
    public void The_edge_offset_and_scale_settings_set_the_two_layout_variables_and_clamp_scale()
    {
        Engine engine = ThemeEngine();

        Dictionary<string, string> styles = Applied(engine, "{ edgeOffset: 40, scale: 150 }");
        styles["--nnz-edge"].Should().Be("40px");
        styles["--nnz-scale"].Should().Be("1.5");

        Applied(engine, "{ scale: 900 }")["--nnz-scale"].Should().Be("2");
        Applied(engine, "{ scale: 10 }")["--nnz-scale"].Should().Be("0.5");
        Applied(engine, "{}").Keys.Should().NotContain(["--nnz-edge", "--nnz-scale"]);
    }

    // Audit 2026-11-26: the fit zoom made the page 1.239x, but 100vw inside it stayed 1280 px, so the Chat
    // Box drew 1586 px wide and its cards were cut off on the right. The SDK hands the widgets one percent of
    // the browser source in zoomed pixels, the unit a widget uses instead of vw and vh.
    [Fact]
    public void The_fit_zoom_gives_the_widgets_one_percent_of_the_browser_source_in_zoomed_pixels()
    {
        Engine engine = ThemeEngine();
        engine.Execute("var window = { innerWidth: 1280, innerHeight: 720 };");

        Dictionary<string, string> styles = Applied(
            engine,
            "{ designWidth: 1920, designHeight: 1080 }"
        );
        styles["zoom"].Should().Be("0.6666666666666666");
        styles["--nnz-vw"].Should().Be("19.2px");
        styles["--nnz-vh"].Should().Be("10.8px");

        Applied(engine, "{}").Keys.Should().NotContain(["zoom", "--nnz-vw", "--nnz-vh"]);
    }

    // Stoney, live 2026-11-26: "i picked right bottom indeed, and its left aligned... its not moving to any
    // corner". A 680 x 180 Now Playing in a bigger browser source zoomed up until it filled the source, so no
    // anchor could move it (and the TTS card filled the canvas the same way). The fit only shrinks.
    [Fact]
    public void The_fit_zoom_never_grows_a_widget_past_its_design_size_so_the_anchor_can_place_it()
    {
        Engine engine = ThemeEngine();
        engine.Execute("var window = { innerWidth: 1920, innerHeight: 1080 };");

        Dictionary<string, string> styles = Applied(
            engine,
            "{ designWidth: 680, designHeight: 180 }"
        );
        styles["zoom"].Should().Be("1");
        styles["--nnz-vw"].Should().Be("19.2px");
        styles["--nnz-vh"].Should().Be("10.8px");
    }

    [Fact]
    public void No_first_party_widget_sizes_itself_by_the_raw_viewport_that_the_fit_zoom_does_not_follow()
    {
        Assembly assets = typeof(FirstPartyWidgetCatalogue).Assembly;
        List<string> offenders = [];
        foreach (string name in assets.GetManifestResourceNames().Where(n => n.EndsWith(".vue")))
        {
            using Stream stream = assets.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            string source = reader.ReadToEnd();
            offenders.AddRange(
                Regex
                    .Matches(source, @"(?<!var\(--nnz-v[wh], )(?<![\w.])[\d.]+v(w|h|min|max)\b")
                    .Select(m => $"{name}: {m.Value}")
            );
        }

        offenders
            .Should()
            .BeEmpty(
                "a widget writes calc(N * var(--nnz-vw, 1vw)) and calc(N * var(--nnz-vh, 1vh))"
            );
    }

    // Live 2026-11-26: Now Playing's box set width: calc(100% / var(--nnz-z)). Chromium 128+ already resolves a
    // zoomed box's percentage against its zoomed share, so the zoom was divided out twice and the card covered
    // two thirds of its slot, on the left. A zoomed part sizes by auto width or by --nnz-vw, never by % / zoom.
    [Fact]
    public void No_first_party_widget_divides_a_percentage_by_the_zoom()
    {
        Assembly assets = typeof(FirstPartyWidgetCatalogue).Assembly;
        List<string> offenders = [];
        foreach (string name in assets.GetManifestResourceNames().Where(n => n.EndsWith(".vue")))
        {
            using Stream stream = assets.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            string source = reader.ReadToEnd();
            offenders.AddRange(
                Regex
                    .Matches(source, @"[\d.]+%\s*/\s*var\(--nnz-(z|own-zoom)\)")
                    .Select(m => $"{name}: {m.Value}")
            );
        }

        offenders.Should().BeEmpty("a percentage on a zoomed part is already in zoomed pixels");
    }
}
