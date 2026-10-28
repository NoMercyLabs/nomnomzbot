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
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves chat_box.vue puts a chatter's 7TV paint on the message HEADER BAR (not on the name text), keeps the
/// name and pronoun text plain black or white by contrast against that bar, and falls back to the Twitch name
/// colour when there is no paint (or when <c>showSevenTvPaints</c> is off). The real <c>headStyle</c> and its
/// helpers are extracted verbatim (balanced-brace) from the shipped embedded asset and run in a Jint engine.
/// </summary>
public sealed class ChatBoxHeaderPaintTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] Functions =
    [
        "hexLuminance",
        "contrastRatio",
        "readableTextColor",
        "headColors",
        "headStyle",
    ];

    private static string LoadChatBoxAsset()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        const string resourceName = "NomNomzBot.Infrastructure.Content.Widgets.Assets.chat_box.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the embedded asset '{resourceName}' should exist");
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    private static string ExtractFunction(string source, string functionName)
    {
        Match start = Regex.Match(source, $@"function\s+{Regex.Escape(functionName)}\s*\(");
        start.Success.Should().BeTrue($"'{functionName}' must exist in chat_box.vue");

        int braceOpen = source.IndexOf('{', source.IndexOf(')', start.Index));
        int depth = 0;
        int i = braceOpen;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }
        i.Should().BeLessThan(source.Length, $"'{functionName}' body must close its braces");
        return source[start.Index..(i + 1)];
    }

    // Strips the small set of TS annotations these helpers use so Jint (a plain ES engine) can parse them.
    private static string StripTypes(string text)
    {
        string noReturn = Regex.Replace(
            text,
            @"\)\s*:\s*(?:string\[\]|Record<string, string>|string|number)\s*\{",
            ") {"
        );
        return Regex.Replace(
            noReturn,
            @"(?<=[\w\)])\s*:\s*(?:string\[\]|number\[\]|Record<string, string>|ChatLine|string|number|boolean)(?=[,\)=\s])",
            ""
        );
    }

    private static Engine BuildEngine(bool paintsOn = true)
    {
        string source = LoadChatBoxAsset();
        Engine engine = new();
        engine.Execute($"var cfg = {{ showSevenTvPaints: {(paintsOn ? "true" : "false")} }};");
        foreach (string name in Functions)
            engine.Execute(StripTypes(ExtractFunction(source, name)));
        return engine;
    }

    private static IDictionary<string, object> HeadStyle(Engine engine, object line)
    {
        engine.SetValue("__lineJson", JsonSerializer.Serialize(line, CamelCase));
        return (IDictionary<string, object>)
            engine.Evaluate("headStyle(JSON.parse(__lineJson))").ToObject()!;
    }

    private static object Line(string? twitchColor, object? paint) =>
        new { Color = twitchColor ?? "", Paint = paint };

    private static object GradientPaint(string gradient, string firstStop) =>
        new
        {
            BackgroundImage = gradient,
            Color = firstStop,
            TextShadow = (string?)null,
            IsImageOnly = false,
        };

    [Fact]
    public void Gradient_paint_fills_the_header_bar_and_text_is_picked_by_contrast()
    {
        Engine engine = BuildEngine();
        object line = Line(
            "#ff0000",
            GradientPaint("linear-gradient(90deg, #FFA500FF 0%, #FFD27FFF 100%)", "#FFA500FF")
        );

        IDictionary<string, object> style = HeadStyle(engine, line);

        style["background-image"]
            .Should()
            .Be("linear-gradient(90deg, #FFA500FF 0%, #FFD27FFF 100%)");
        style["color"].Should().Be("#000000", "black reads better on a light orange bar");
        style["-webkit-text-fill-color"].Should().Be("#000000");
    }

    [Fact]
    public void Temptic404_city_lights_paint_is_applied_exactly_as_7tv_defines_it()
    {
        // "City Lights", the live 7TV paint of temptic404 (flattened by SevenTvPaintMapper).
        const string gradient =
            "linear-gradient(360deg, #FE019AFF 25%, #FC708EFF 40%, #F5A67CFF 50%, #E7D45EFF 59%, #CFFF04FF 80%)";
        Engine engine = BuildEngine();

        IDictionary<string, object> style = HeadStyle(
            engine,
            Line("#ff0000", GradientPaint(gradient, "#FE019AFF"))
        );

        style["background-image"].Should().Be(gradient);
        style
            .Keys.Should()
            .NotContain(
                ["background-size", "background-position", "background-repeat", "background-color"],
                "7TV does not override size, position or repeat of its own gradient"
            );
        style["color"].Should().Be("#000000");
    }

    [Fact]
    public void Dark_paint_gets_white_text()
    {
        Engine engine = BuildEngine();
        object paint = new
        {
            BackgroundImage = (string?)null,
            Color = "#1A1A66FF",
            TextShadow = (string?)null,
            IsImageOnly = false,
        };

        IDictionary<string, object> style = HeadStyle(engine, Line("#ff0000", paint));

        style["background-color"].Should().Be("#1A1A66FF");
        style["color"].Should().Be("#ffffff");
    }

    [Fact]
    public void Gradient_with_a_dark_and_a_light_stop_picks_the_best_worst_case_text()
    {
        Engine engine = BuildEngine();
        object line = Line(
            null,
            GradientPaint("linear-gradient(90deg, #000000FF 0%, #FFFFFFFF 100%)", "#000000FF")
        );

        IDictionary<string, object> style = HeadStyle(engine, line);

        style["color"].Should().BeOneOf("#000000", "#ffffff");
        style.Should().ContainKey("background-image");
    }

    [Fact]
    public void No_paint_falls_back_to_the_twitch_name_colour_as_the_bar()
    {
        Engine engine = BuildEngine();

        IDictionary<string, object> style = HeadStyle(engine, Line("#9146ff", null));

        style["background-color"].Should().Be("#9146ff");
        style.Should().NotContainKey("background-image");
        style["color"].Should().Be("#ffffff");
    }

    [Fact]
    public void Paints_setting_off_ignores_the_paint_and_uses_the_twitch_colour()
    {
        Engine engine = BuildEngine(paintsOn: false);
        object line = Line(
            "#ffff00",
            GradientPaint("linear-gradient(#ff0000, #0000ff)", "#ff0000")
        );

        IDictionary<string, object> style = HeadStyle(engine, line);

        style["background-color"].Should().Be("#ffff00");
        style.Should().NotContainKey("background-image");
        style["color"].Should().Be("#000000");
    }

    [Fact]
    public void No_paint_and_no_colour_leaves_the_header_unstyled()
    {
        Engine engine = BuildEngine();

        IDictionary<string, object> style = HeadStyle(engine, Line(null, null));

        style.Should().BeEmpty();
    }
}
