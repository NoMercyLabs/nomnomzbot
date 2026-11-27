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

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>NomNomz.image(url, cssPx)</c> routes a widget image through the overlay image endpoint at the size the
/// widget draws it, and one document-level error listener falls a failed optimized image back to its original
/// source, once. The editor preview SDK carries the same helper, so both run the same cases.
/// </summary>
public sealed class OverlaySdkImageTests
{
    private const string Optimizer = "/api/v1/overlay/images?src=";

    private const string PreviewWindow = """
        var window = this;
        window.addEventListener = function () {};
        window.parent = { postMessage: function () {} };
        var location = { search: "", protocol: "https:", host: "bot.test", origin: "https://bot.test" };
        var __documentListeners = [];
        var document = { addEventListener: function (type, handler, capture) { __documentListeners.push({ type: type, handler: handler, capture: capture }); } };
        """;

    private static Engine Live() => OverlaySdkRuntime.Connected().Engine;

    private static Engine Preview(string window = PreviewWindow)
    {
        Engine engine = new();
        engine.Execute(window);
        engine.Execute(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "editor", "preview-sdk.js")
            )
        );
        return engine;
    }

    public static TheoryData<string> Sdks => new() { "live", "preview" };

    private static Engine Pick(string sdk) => sdk == "live" ? Live() : Preview();

    private static string Image(Engine engine, string url, string cssPx) =>
        engine.Evaluate($"NomNomz.image({url}, {cssPx})").ToString();

    private static string Wrapped(string absolute, int width) =>
        Optimizer + Uri.EscapeDataString(absolute) + "&w=" + width;

    [Theory]
    [MemberData(nameof(Sdks))]
    public void An_https_image_goes_through_the_optimizer_at_double_the_drawn_width(string sdk)
    {
        Engine engine = Pick(sdk);

        Image(engine, "'https://static-cdn.jtvnw.net/emoticons/v2/1/default/dark/3.0'", "28")
            .Should()
            .Be(Wrapped("https://static-cdn.jtvnw.net/emoticons/v2/1/default/dark/3.0", 56));
    }

    [Theory]
    [MemberData(nameof(Sdks))]
    public void A_relative_image_resolves_against_the_page_origin_before_it_is_wrapped(string sdk)
    {
        Engine engine = Pick(sdk);

        Image(engine, "'/api/v1/uploads/avatar.png?v=2'", "40")
            .Should()
            .Be(Wrapped("https://bot.test/api/v1/uploads/avatar.png?v=2", 80));
    }

    [Theory]
    [MemberData(nameof(Sdks))]
    public void A_width_is_rounded_up_and_kept_between_1_and_1024(string sdk)
    {
        Engine engine = Pick(sdk);
        const string url = "'https://cdn.7tv.app/emote/x/4x.webp'";

        Image(engine, url, "1.2").Should().EndWith("&w=3");
        Image(engine, url, "0.1").Should().EndWith("&w=1");
        Image(engine, url, "900").Should().EndWith("&w=1024");
    }

    [Theory]
    [MemberData(nameof(Sdks))]
    public void An_image_that_must_not_be_rewritten_comes_back_unchanged(string sdk)
    {
        Engine engine = Pick(sdk);
        string[] untouched =
        [
            "",
            "data:image/png;base64,AAAA",
            "blob:https://bot.test/1234",
            "https://cdn.example.com/logo.svg",
            "https://cdn.example.com/logo.SVG?v=3",
            "/uploads/logo.svg#fragment",
            "http://cdn.example.com/plain.png",
            "/api/v1/overlay/images?src=https%3A%2F%2Fa.test%2Fx.png&w=64",
            "https://bot.test/api/v1/overlay/images?src=https%3A%2F%2Fa.test%2Fx.png&w=64",
        ];

        foreach (string url in untouched)
            Image(engine, $"'{url}'", "40").Should().Be(url);
        engine.Evaluate("NomNomz.image(null, 40) === ''").AsBoolean().Should().BeTrue();
        engine.Evaluate("NomNomz.image(undefined, 40) === ''").AsBoolean().Should().BeTrue();
    }

    /// <summary>Guard: every system widget image binding draws through <c>sized(...)</c>, never the raw URL.</summary>
    [Theory]
    [InlineData("alerts", ":src=\"sized(current.imageUrl, 240)\"")]
    [InlineData("chat_box", ":src=\"sized(l.avatarUrl, cfg.fontSize * 1.25)\"")]
    [InlineData("chat_box", ":src=\"badgeSrc(b)\"")]
    [InlineData("chat_box", ":src=\"sized(l.card.imageUrl, 40)\"")]
    [InlineData("chat_box", ":src=\"emoteSrc(fr)\"")]
    [InlineData("chat_box", ":src=\"cheermoteSrc(fr)\"")]
    [InlineData("chat_box", ":src=\"sized(gifUrl(fr), 420)\"")]
    [InlineData("chat_box", ":src=\"sized(fr.linkPreview.imageUrl, 40)\"")]
    [InlineData("chat_box", "v-html=\"htmlWithSizedImages(fr.text)\"")]
    [InlineData("drop_game", ":src=\"sized(f.image.src, slots.payload.size.w)\"")]
    [InlineData("emote_wall", ":src=\"sized(e.url, e.size)\"")]
    [InlineData("lucky_feather", ":src=\"sized(holder.avatarUrl, 32)\"")]
    [InlineData("now_playing", ":src=\"sized(artUrl, 96)\"")]
    [InlineData("raid_recap", ":src=\"sized(avatarUrl, 128)\"")]
    [InlineData("raid_recap", ":src=\"sized(avatarUrl, 256)\"")]
    [InlineData("redemption_alert", ":src=\"sized(current.avatarUrl, 44)\"")]
    public void A_system_widget_draws_each_image_through_the_sdk_helper(
        string widget,
        string binding
    )
    {
        string source = File.ReadAllText(Hubs.WidgetAssetPaths.VueFile(widget));

        source.Should().Contain(binding);
        source.Should().Contain("NomNomz.image(url, cssPx)");
    }

    [Fact]
    public void A_preview_page_with_no_origin_still_wraps_an_https_image_and_leaves_a_relative_one()
    {
        Engine engine = Preview(
            PreviewWindow.Replace("origin: \"https://bot.test\"", "origin: \"null\"")
        );

        Image(engine, "'https://cdn.example.com/a.png'", "10")
            .Should()
            .Be(Wrapped("https://cdn.example.com/a.png", 20));
        Image(engine, "'/relative/a.png'", "10").Should().Be("/relative/a.png");
    }

    [Theory]
    [MemberData(nameof(Sdks))]
    public void A_failed_optimized_image_falls_back_to_its_original_source_once(string sdk)
    {
        Engine engine = Pick(sdk);
        const string original = "https://static-cdn.jtvnw.net/jtv_user_pictures/a.png";
        engine.Execute(
            $$"""
            var __attrs = {};
            var img = {
              tagName: "IMG",
              src: NomNomz.image("{{original}}", 32),
              getAttribute: function (n) { return n in __attrs ? __attrs[n] : null; },
              setAttribute: function (n, v) { __attrs[n] = String(v); }
            };
            var __errors = __documentListeners.filter(function (l) { return l.type === "error"; });
            var stopped = 0;
            function fail() { __errors.forEach(function (l) { l.handler({ target: img, stopPropagation: function () { stopped++; } }); }); }
            """
        );

        engine.Evaluate("__errors.length").AsNumber().Should().Be(1);
        engine.Evaluate("__errors[0].capture").AsBoolean().Should().BeTrue();
        engine.Evaluate("img.src").ToString().Should().StartWith(Optimizer);

        engine.Execute("fail()");
        engine.Evaluate("img.src").ToString().Should().Be(original);
        engine
            .Evaluate("stopped")
            .AsNumber()
            .Should()
            .Be(1, "the widget's own error handler must not hide an image that is being retried");

        // Vue reuses the element: the next song's art lands on the same img and gets its own retry.
        engine.Execute("img.src = NomNomz.image('https://i.scdn.co/image/next', 32); fail();");
        engine.Evaluate("img.src").ToString().Should().Be("https://i.scdn.co/image/next");
        engine.Evaluate("stopped").AsNumber().Should().Be(2);

        // The same optimized address failing again is not retried a second time; the widget sees it.
        engine.Execute("img.src = NomNomz.image('https://i.scdn.co/image/next', 32); fail();");
        engine
            .Evaluate("img.src")
            .ToString()
            .Should()
            .Be(Wrapped("https://i.scdn.co/image/next", 64));
        engine
            .Evaluate("stopped")
            .AsNumber()
            .Should()
            .Be(2, "a repeat failure of the same address reaches the widget");
    }

    [Theory]
    [MemberData(nameof(Sdks))]
    public void A_failed_image_that_was_never_optimized_or_is_not_an_img_is_left_alone(string sdk)
    {
        Engine engine = Pick(sdk);
        engine.Execute(
            """
            var __errors = __documentListeners.filter(function (l) { return l.type === "error"; });
            function element(tag, src) {
              var attrs = {};
              return {
                tagName: tag, src: src,
                getAttribute: function (n) { return n in attrs ? attrs[n] : null; },
                setAttribute: function (n, v) { attrs[n] = String(v); }
              };
            }
            var plain = element("IMG", "https://cdn.example.com/plain.png");
            var script = element("SCRIPT", NomNomz.image("https://cdn.example.com/a.png", 20));
            var wrapped = script.src;
            __errors.forEach(function (l) { [{ target: plain }, { target: script }, { target: null }].forEach(function (e) { e.stopPropagation = function () { throw new Error("must not stop"); }; l.handler(e); }); });
            """
        );

        engine.Evaluate("plain.src").ToString().Should().Be("https://cdn.example.com/plain.png");
        engine.Evaluate("script.src === wrapped").AsBoolean().Should().BeTrue();
    }
}
