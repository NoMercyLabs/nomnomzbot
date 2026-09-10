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
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves chat_box.vue's <c>showSevenTvPaints</c> setting (S-PL6) actually gates rendering. Server-side paint
/// RESOLUTION is untouched by this slice — the widget always receives <c>line.paint</c> when 7TV resolved one —
/// so the only thing under test is whether the NAME actually picks up that paint. The real <c>paintNameStyle</c>
/// and <c>nameStyle</c> functions are extracted verbatim (balanced-brace, not hand-copied) from the shipped
/// embedded asset and executed in a real Jint engine, so a regression in the actual shipped logic fails this
/// test. <c>contrastColor</c> is stubbed with a marker return value — its own HSL maths are irrelevant to the
/// toggle and are not part of this change; the marker only proves the FALLBACK branch, not the paint branch, ran.
/// </summary>
public sealed class ChatBoxSevenTvPaintToggleTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string LoadChatBoxAsset()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        const string resourceName = "NomNomzBot.Infrastructure.Content.Widgets.Assets.chat_box.vue";
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName);
        stream.Should().NotBeNull($"the embedded asset '{resourceName}' should exist");
        using StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>Balanced-brace extraction of one top-level <c>function name(...) { ... }</c> from real source.</summary>
    private static string ExtractFunction(string source, string functionName)
    {
        Match start = Regex.Match(source, $@"function\s+{Regex.Escape(functionName)}\s*\(");
        start.Success.Should().BeTrue($"'{functionName}' must exist in chat_box.vue");

        int braceOpen = source.IndexOf('{', start.Index);
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

    // Strips the small, known set of TS type annotations these two functions carry so Jint (a plain ES engine)
    // can parse them. Targeted, not a general TS stripper — verified by actually running the extracted code.
    private static string StripKnownTsAnnotations(string functionText) =>
        functionText
            .Replace(
                "function paintNameStyle(p: ChatPaint | null): Record<string, string> {",
                "function paintNameStyle(p) {"
            )
            .Replace("const style: Record<string, string> = {}", "const style = {}")
            .Replace(
                "function nameStyle(l: ChatLine): Record<string, string> {",
                "function nameStyle(l) {"
            );

    private static Engine BuildEngine()
    {
        string source = LoadChatBoxAsset();
        string paintNameStyle = StripKnownTsAnnotations(ExtractFunction(source, "paintNameStyle"));
        string nameStyle = StripKnownTsAnnotations(ExtractFunction(source, "nameStyle"));

        Engine engine = new();
        engine.Execute(
            """
            var cfg = { showSevenTvPaints: true, theme: 'dark' };
            function contrastColor(hex, theme) { return 'FALLBACK:' + hex; }
            """
        );
        engine.Execute(paintNameStyle);
        engine.Execute(nameStyle);
        return engine;
    }

    private static string LineWithPaintJson() =>
        JsonSerializer.Serialize(
            new
            {
                Color = "#ff0000",
                Paint = new
                {
                    BackgroundImage = "linear-gradient(red, blue)",
                    Color = (string?)null,
                    TextShadow = (string?)null,
                    IsImageOnly = false,
                },
            },
            CamelCase
        );

    private static string LineWithoutPaintJson() =>
        JsonSerializer.Serialize(new { Color = "#ff0000", Paint = (object?)null }, CamelCase);

    private static IDictionary<string, object> InvokeNameStyle(Engine engine, string lineJson)
    {
        engine.SetValue("__lineJson", lineJson);
        return (IDictionary<string, object>)
            engine.Evaluate("nameStyle(JSON.parse(__lineJson))").ToObject()!;
    }

    [Fact]
    public void Setting_on_paints_the_name_from_the_resolved_paint()
    {
        Engine engine = BuildEngine();
        engine.Execute("cfg.showSevenTvPaints = true;");

        IDictionary<string, object> style = InvokeNameStyle(engine, LineWithPaintJson());

        style.Should().ContainKey("background-image");
        style["background-image"].Should().Be("linear-gradient(red, blue)");
        style["color"].Should().Be("transparent");
    }

    [Fact]
    public void Setting_off_falls_back_to_the_plain_chat_colour_even_though_a_paint_resolved()
    {
        Engine engine = BuildEngine();
        engine.Execute("cfg.showSevenTvPaints = false;");

        IDictionary<string, object> style = InvokeNameStyle(engine, LineWithPaintJson());

        style.Should().NotContainKey("background-image");
        style["color"].Should().Be("FALLBACK:#ff0000");
    }

    [Fact]
    public void No_resolved_paint_falls_back_regardless_of_the_setting()
    {
        Engine engine = BuildEngine();
        engine.Execute("cfg.showSevenTvPaints = true;");

        IDictionary<string, object> style = InvokeNameStyle(engine, LineWithoutPaintJson());

        style.Should().NotContainKey("background-image");
        style["color"].Should().Be("FALLBACK:#ff0000");
    }
}
