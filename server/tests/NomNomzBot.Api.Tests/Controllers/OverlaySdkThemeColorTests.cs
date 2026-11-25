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
        styles.Keys.Where(k => k is "--nnz-accent" or "--nnz-surface").Should().Equal(mainVariable);
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
}
