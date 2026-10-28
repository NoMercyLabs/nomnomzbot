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

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Jint;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the first-party chat_overlay bundle puts a chatter's 7TV paint on the NAME BAR background (the banner,
/// not the name text), keeps the name text plain black or white by contrast against that bar, and leaves the bar to
/// the chat-colour gradient of the stylesheet when there is no paint. Stoney asked five times for exactly this on
/// his live "Twitch Chat Overlay". The real <c>bannerPaintStyle</c> and its helpers are extracted verbatim
/// (balanced-brace) from the shipped embedded ChatMessage.vue and run in a Jint engine, the same way
/// <see cref="ChatBoxHeaderPaintTests"/> guards chat_box.vue.
/// </summary>
public sealed class ChatOverlayBannerPaintTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string ChatMessagePath = "components/ChatMessage.vue";

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] Functions =
    [
        "tooLight",
        "hasPaint",
        "paintColors",
        "bannerPaintStyle",
    ];

    private readonly VueSfcCompilerFixture _fixture;

    public ChatOverlayBannerPaintTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private static string ChatMessageSource()
    {
        Dictionary<string, string> files = FirstPartyWidgetBundles.Read("chat_overlay");
        files.Should().ContainKey(ChatMessagePath);
        return files[ChatMessagePath];
    }

    private static string ExtractFunction(string source, string functionName)
    {
        Match start = Regex.Match(source, $@"function\s+{Regex.Escape(functionName)}\s*\(");
        start
            .Success.Should()
            .BeTrue($"'{functionName}' must exist in chat_overlay ChatMessage.vue");

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
            @"\)\s*:\s*(?:string\[\]|Record<string, string>|string|number|boolean)\s*\{",
            ") {"
        );
        return Regex.Replace(
            noReturn,
            @"(?<=[\w\)])\s*:\s*(?:NnzChatPaintDto(?:\s*\|\s*null)?|string\[\]|Record<string, string>|string|number|boolean)(?=[,\)=\s])",
            ""
        );
    }

    private static Engine BuildEngine(object line)
    {
        string source = ChatMessageSource();
        Engine engine = new();
        engine.SetValue("__lineJson", JsonSerializer.Serialize(line, CamelCase));
        engine.Execute("var props = { line: JSON.parse(__lineJson) };");
        foreach (string name in Functions)
            engine.Execute(StripTypes(ExtractFunction(source, name)));
        return engine;
    }

    private static IDictionary<string, object> BannerStyle(object line) =>
        (IDictionary<string, object>)BuildEngine(line).Evaluate("bannerPaintStyle()").ToObject()!;

    private static object Line(string? chatColor, object? paint) =>
        new { Color = chatColor ?? "", Paint = paint };

    private static object GradientPaint(string gradient, string? shadow = null) =>
        new
        {
            BackgroundImage = gradient,
            Color = (string?)null,
            TextShadow = shadow,
            IsImageOnly = false,
        };

    [Fact]
    public void Painted_viewer_gets_the_paint_on_the_name_bar_background()
    {
        // "City Lights", the live 7TV paint of temptic404 (flattened by SevenTvPaintMapper).
        const string gradient =
            "linear-gradient(360deg, #FE019AFF 25%, #FC708EFF 40%, #F5A67CFF 50%, #E7D45EFF 59%, #CFFF04FF 80%)";

        IDictionary<string, object> style = BannerStyle(
            Line("#ff8800", GradientPaint(gradient, "0 0 4px #FF00BB"))
        );

        style["background-image"].Should().Be(gradient, "the paint IS the bar background");
        style["background-color"]
            .Should()
            .Be("#FE019AFF", "the first stop backs the bar while the image loads");
        style["box-shadow"]
            .Should()
            .Be("0 0 4px #FF00BB", "the paint's own shadow glows around the bar");
        style
            .Should()
            .NotContainKey(
                "background-clip",
                "the paint is on the bar, not clipped to the name text"
            );
    }

    [Fact]
    public void Name_text_on_the_painted_bar_is_black_on_a_light_paint_and_white_on_a_dark_paint()
    {
        IDictionary<string, object> light = BannerStyle(
            Line(null, GradientPaint("linear-gradient(90deg, #FFA500FF 0%, #FFD27FFF 100%)"))
        );
        IDictionary<string, object> dark = BannerStyle(
            Line(null, GradientPaint("linear-gradient(90deg, #1A0033FF 0%, #330066FF 100%)"))
        );

        light["color"].Should().Be("#000");
        dark["color"].Should().Be("#fff");
    }

    [Fact]
    public void Viewer_without_a_paint_keeps_the_chat_colour_bar()
    {
        IDictionary<string, object> style = BannerStyle(Line("#ff8800", null));

        style.Should().BeEmpty("no inline style means the stylesheet's chat-colour gradient wins");
    }

    [Fact]
    public void Template_binds_the_banner_style_to_the_paint_function()
    {
        Regex banner = new(
            @"<div class=""banner""[^>]*:style=""bannerPaintStyle\(\)""",
            RegexOptions.Singleline
        );

        banner
            .IsMatch(ChatMessageSource())
            .Should()
            .BeTrue("the name bar must wear the paint style");
        ChatMessageSource()
            .Should()
            .NotContain("paintNameStyle", "the name text is no longer painted");
    }

    [Fact]
    public void Bundle_is_a_complete_project_whose_every_vue_file_compiles()
    {
        Dictionary<string, string> files = FirstPartyWidgetBundles.Read("chat_overlay");

        files
            .Keys.Should()
            .Contain([
                "index.vue",
                "settings.json",
                "components/ChatMessage.vue",
                "components/MessageFragment.vue",
                "components/OgPreview.vue",
                "components/SongCard.vue",
            ]);

        foreach (KeyValuePair<string, string> file in files.Where(f => f.Key.EndsWith(".vue")))
        {
            Result<VueSfcOutput> result = _fixture.Compiler.Compile(file.Value, file.Key);
            result.IsSuccess.Should().BeTrue($"{file.Key} must compile: {result.ErrorMessage}");
        }
    }
}
