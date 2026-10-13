// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Guards <see cref="ChatHtmlSanitizer"/> — the boundary that makes a viewer's inline chat HTML safe to render — against
/// the two failure modes that matter, using markup taken verbatim from the real chat-history corpus:
/// (1) it must neutralise every attack a sender actually tried (script/style subtrees, event handlers, off-scheme urls,
/// framing) and every on-overlay abuse vector (video and audio); and (2) it must NOT over-strip the harmless fun the
/// feature exists for (marquee with its motion/colour attributes, images, formatting, tables). Video and audio are not allowed.
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

    [Theory]
    [InlineData(
        @"a<video controls autoplay poster=""https://cdn.example/p.jpg""><source src=""https://cdn.example/v.mp4"" type=""video/mp4""><track src=""https://cdn.example/t.vtt""></video>b",
        "ab"
    )]
    [InlineData(
        @"a<audio controls><source src=""https://cdn.example/a.mp3"" type=""audio/mp3""></audio>b",
        "ab"
    )]
    [InlineData(@"a<source src=""https://cdn.example/a.mp3"">b", "ab")]
    [InlineData(@"a<track src=""https://cdn.example/t.vtt"" kind=""subtitles"">b", "ab")]
    [InlineData(@"a<video src=""http://cdn.example/v.mp4""></video>b", "ab")]
    public void Video_and_audio_are_removed_whole_with_no_leftover(string input, string expected)
    {
        ChatHtmlSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void An_img_keeps_its_https_source_but_loses_the_event_handler()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<img src=""https://cdn.example/a.gif"" onerror=""alert(1)"">"
        );

        clean.Should().Be(@"<img src=""https://cdn.example/a.gif"">");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>hi", "hi")]
    [InlineData(@"<iframe src=""https://evil.example""></iframe>hi", "hi")]
    [InlineData(@"<svg><script>alert(1)</script></svg>hi", "hi")]
    [InlineData(@"<a href=""javascript:alert(1)"">x</a>", "<a>x</a>")]
    [InlineData(@"<a href=""vbscript:msgbox(1)"">x</a>", "<a>x</a>")]
    [InlineData(@"<a href=""data:text/html,<script>alert(1)</script>"">x</a>", "<a>x</a>")]
    [InlineData(@"<img src=""javascript:alert(1)"">hi", "hi")]
    public void Nothing_that_executes_survives(string input, string expected)
    {
        ChatHtmlSanitizer.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void An_img_with_a_disallowed_scheme_source_is_removed_not_left_captioned_and_blank()
    {
        // Regression for the priority bug reported 2026-09-01: a viewer's OS/browser GIF picker pasted an <img>
        // whose src used a non-https scheme (http:// or otherwise disallowed). AllowedSchemes strips the src, but
        // the tag used to survive with only its alt text ("Cat Festival GIF by W&W") — the overlay then showed the
        // caption with a broken image underneath it instead of nothing at all. The whole <img> must go with its src.
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<img src=""http://media.example.com/cat-festival.gif"" alt=""Cat Festival GIF by W&amp;W"">"
        );

        clean.Should().NotContain("http://").And.NotContain("<img");
    }

    [Fact]
    public void An_img_with_an_https_source_still_survives_intact()
    {
        // Guards the fix above against over-stripping: a genuinely valid https media url must still render.
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<img src=""https://media.example.com/cat-festival.gif"" alt=""Cat Festival GIF by W&amp;W"">"
        );

        clean
            .Should()
            .Contain("<img")
            .And.Contain(@"src=""https://media.example.com/cat-festival.gif""");
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
    public void Inline_css_sizes_an_image_the_way_the_sender_wrote_it()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<img src=""https://media.giphy.com/media/x/giphy.gif"" style=""width: 200px; height: 100px; object-fit: cover"">"
        );

        clean
            .Should()
            .Contain(@"style=""width: 200px; height: 100px; object-fit: cover""")
            .And.Contain(@"src=""https://media.giphy.com/media/x/giphy.gif""");
    }

    [Fact]
    public void Text_and_colour_css_survives()
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            @"<span style=""color: red; background-color: #000; font-weight: bold; text-shadow: 1px 1px 2px black"">hi</span>"
        );

        clean
            .Should()
            .Contain(
                @"style=""color: rgba(255, 0, 0, 1); background-color: rgba(0, 0, 0, 1); font-weight: bold; text-shadow: 1px 1px 2px rgba(0, 0, 0, 1)"""
            );
    }

    [Theory]
    [InlineData("width: 5000px", "width: 500px")]
    [InlineData("height: 4000px", "height: 300px")]
    [InlineData("max-height: 90em", "max-height: 18em")]
    [InlineData("font-size: 400px", "font-size: 48px")]
    [InlineData("line-height: 40", "line-height: 3")]
    [InlineData("letter-spacing: 300px", "letter-spacing: 10px")]
    public void An_oversized_css_length_is_capped_so_one_message_cannot_cover_the_overlay(
        string declaration,
        string capped
    )
    {
        string clean = ChatHtmlSanitizer.Sanitize($@"<div style=""{declaration}"">x</div>");

        clean.Should().Contain($@"style=""{capped}""");
    }

    [Theory]
    [InlineData("width: 100vw")]
    [InlineData("height: calc(100vh + 10px)")]
    [InlineData("width: 500px !important")]
    public void A_css_length_the_cap_cannot_read_is_dropped(string declaration)
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            $@"<div style=""{declaration}; color: red"">x</div>"
        );

        clean.Should().Contain(@"<div style=""color: rgba(255, 0, 0, 1)"">x</div>");
    }

    [Theory]
    [InlineData("position: fixed")]
    [InlineData("z-index: 9999")]
    [InlineData("background-image: url(https://evil.example/a.png)")]
    [InlineData("background: url(https://evil.example/a.png)")]
    [InlineData("transform: scale(50)")]
    public void Layout_escaping_and_url_css_is_dropped(string declaration)
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            $@"<div style=""{declaration}; color: red"">x</div>"
        );

        clean.Should().Contain(@"<div style=""color: rgba(255, 0, 0, 1)"">x</div>");
    }

    [Theory]
    [InlineData("500px", "500")]
    [InlineData("500p", "500")]
    [InlineData("5000", "500")]
    public void A_width_attribute_is_read_as_a_number_and_capped(string width, string expected)
    {
        string clean = ChatHtmlSanitizer.Sanitize(
            $@"<img width=""{width}"" height=""9000"" src=""https://media.giphy.com/media/x/giphy.gif"">"
        );

        clean.Should().Contain($@"width=""{expected}""").And.Contain(@"height=""300""");
    }

    [Fact]
    public void Concurrent_messages_each_drop_only_their_own_off_scheme_media()
    {
        // The sanitiser is one shared instance; chat from many channels runs through it at the same time.
        ConcurrentBag<string> wrong = [];

        Parallel.For(
            0,
            20_000,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            i =>
            {
                bool offScheme = i % 2 == 0;
                string html = offScheme
                    ? $@"<p>{i}<img src=""http://x.example/{i}.gif""></p>"
                    : $@"<p>{i}<img src=""https://x.example/{i}.gif""></p>";
                string expected = offScheme
                    ? $"<p>{i}</p>"
                    : $@"<p>{i}<img src=""https://x.example/{i}.gif""></p>";

                string clean = ChatHtmlSanitizer.Sanitize(html);
                if (clean != expected)
                    wrong.Add($"{i}: {clean}");
            }
        );

        wrong.Should().BeEmpty();
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
