// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Guards <see cref="ChatHtmlSanitizer"/> — the boundary that makes a viewer's inline chat HTML safe to render — against
/// the two failure modes that matter, using markup taken verbatim from the real chat-history corpus:
/// (1) it must neutralise every attack a sender actually tried (script/style subtrees, event handlers, off-scheme urls,
/// framing) and every on-overlay abuse vector (force-playing media); and (2) it must NOT over-strip the harmless fun the
/// feature exists for (marquee with its motion/colour attributes, display-only audio/video, formatting, tables).
/// </summary>
public sealed class ChatHtmlSanitizerTests
{
    [Fact]
    public void Strips_an_event_handler_but_keeps_the_element()
    {
        // Verbatim from the corpus — an actual attempted overlay XSS.
        string clean = ChatHtmlSanitizer.Sanitize(@"<img onerror=""alert('hello i am hacked')"">");

        clean.Should().Contain("<img");
        clean.ToLowerInvariant().Should().NotContain("onerror").And.NotContain("alert");
    }

    [Fact]
    public void Removes_a_style_block_and_its_css_but_keeps_the_content_element()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<style>.a{color:red;}</style><div class=""a"">aaaaaaaaa</div>"
        );

        clean.ToLowerInvariant().Should().NotContain("<style").And.NotContain("color:red");
        clean.Should().Contain(@"<div class=""a"">aaaaaaaaa</div>");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>hi", "script")]
    [InlineData("<script>alert(1)</script>hi", "alert")]
    [InlineData(@"<iframe src=""https://evil.example""></iframe>", "iframe")]
    [InlineData(@"<a href=""javascript:alert(1)"">x</a>", "javascript")]
    [InlineData(@"<div style=""background:url(x)"">y</div>", "style")]
    public void Dangerous_markup_leaves_no_trace(string input, string forbidden)
    {
        ChatHtmlSanitizer.Sanitize(input).ToLowerInvariant().Should().NotContain(forbidden);
    }

    [Fact]
    public void Media_is_display_only_never_force_playing()
    {
        // The tags are allowed (the streamer asked for audio/video), but the sanitiser must drop the attributes that let
        // a sender blast media onto an OBS source with nobody there to click stop.
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<audio controls autoplay loop preload=""auto""><source src=""https://cdn.example/awesome.mp3"" type=""audio/mp3""></audio>"
        );

        clean.ToLowerInvariant().Should().Contain("<audio").And.Contain("<source");
        clean.Should().Contain(@"src=""https://cdn.example/awesome.mp3""");
        clean
            .ToLowerInvariant()
            .Should()
            .NotContain("autoplay")
            .And.NotContain("loop")
            .And.NotContain("preload");
    }

    [Fact]
    public void An_http_media_source_is_dropped_to_keep_urls_https_only()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<video controls poster=""http://cdn.example/p.jpg""><source src=""http://cdn.example/v.mp4"" type=""video/mp4""></video>"
        );

        clean.ToLowerInvariant().Should().Contain("<video");
        clean.Should().NotContain("http://");
    }

    [Fact]
    public void Marquee_keeps_its_motion_and_colour_attributes_but_not_inline_css()
    {
        // Verbatim shape from the corpus (deduped attributes): the fun must survive, the style attribute must not.
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<marquee direction=""down"" width=""250"" height=""200"" behavior=""alternate"" style=""border:solid"" bgcolor=""#3f216a"" scrollamount=""1"">hi</marquee>"
        );

        clean.Should().Contain("<marquee");
        clean
            .Should()
            .Contain(@"direction=""down""")
            .And.Contain(@"behavior=""alternate""")
            .And.Contain(@"bgcolor=""#3f216a""")
            .And.Contain(@"scrollamount=""1""");
        clean.ToLowerInvariant().Should().NotContain("style=");
    }

    [Fact]
    public void A_marquee_wrapping_an_image_survives_intact()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<marquee><img src=""https://cdn.nomercy.tv/logo.svg"" height=""40"">quote</marquee>"
        );

        clean.Should().Contain("<marquee").And.Contain("<img");
        clean.Should().Contain(@"src=""https://cdn.nomercy.tv/logo.svg""");
    }
}
