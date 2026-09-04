// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using FluentAssertions;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// 7TV paints flattened to CSS. The shapes here are taken from the LIVE v4 API (2026-09-04): of 1,024
/// paints the layer kinds actually in use are linear gradient (622), radial gradient (264) and image (131),
/// and "Ginger Tabby S" below is a real paint copied verbatim.
/// </summary>
public sealed class SevenTvPaintMapperTests
{
    private static SevenTvPaintMapper.Layer Linear(
        double angle,
        bool repeating,
        params (double At, string Hex)[] stops
    ) =>
        new(
            "PaintLayerTypeLinearGradient",
            Angle: angle,
            Repeating: repeating,
            Stops: [.. stops.Select(s => new SevenTvPaintMapper.Stop(s.At, s.Hex))]
        );

    [Fact]
    public void A_real_linear_gradient_paint_becomes_a_css_gradient()
    {
        // "Ginger Tabby S", verbatim from the live API.
        ChatPaint? paint = SevenTvPaintMapper.Map(
            "01GHA42XER0001KT343YQ6DM2E",
            "Ginger Tabby S",
            [Linear(66, repeating: true, (0.57, "#CA804EFF"), (0.65, "#503611FF"))],
            []
        );

        paint.Should().NotBeNull();
        paint
            .BackgroundImage.Should()
            .Be("repeating-linear-gradient(66deg, #CA804EFF 57%, #503611FF 65%)");
        // The first stop doubles as the flat fallback so a surface that cannot paint a gradient still gets
        // a colour from the same paint rather than the chatter's default.
        paint.Color.Should().Be("#CA804EFF");
        paint.IsImageOnly.Should().BeFalse();
    }

    [Fact]
    public void A_non_repeating_gradient_does_not_claim_to_repeat()
    {
        ChatPaint? paint = SevenTvPaintMapper.Map(
            "p",
            "n",
            [Linear(90, repeating: false, (0, "#FFFFFFFF"), (1, "#000000FF"))],
            []
        );

        paint!.BackgroundImage.Should().StartWith("linear-gradient(");
        paint.BackgroundImage.Should().NotContain("repeating");
    }

    [Fact]
    public void A_radial_gradient_keeps_its_shape_and_falls_back_to_circle_when_unknown()
    {
        // An unrecognised shape must not be passed through: CSS drops the WHOLE declaration on a bad value,
        // so the paint would vanish rather than degrade.
        SevenTvPaintMapper.Layer weird = new(
            "PaintLayerTypeRadialGradient",
            Shape: "hexagon",
            Stops: [new(0, "#FF0000FF"), new(1, "#0000FFFF")]
        );

        SevenTvPaintMapper
            .Map("p", "n", [weird], [])!
            .BackgroundImage.Should()
            .Be("radial-gradient(circle, #FF0000FF 0%, #0000FFFF 100%)");

        SevenTvPaintMapper.Layer ellipse = weird with { Shape = "ellipse" };
        SevenTvPaintMapper
            .Map("p", "n", [ellipse], [])!
            .BackgroundImage.Should()
            .Contain("ellipse");
    }

    [Fact]
    public void A_real_radial_gradient_paint_becomes_a_css_gradient()
    {
        // "Mochi", verbatim from the live v4 API (2026-09-04) — the paint LanyDelRey (twitch id 80775337)
        // actually wears, resolved live rather than invented.
        ChatPaint? paint = SevenTvPaintMapper.Map(
            "01JFT4JF6ETXCMHDEAVEH7PJJ1",
            "Mochi",
            [
                new(
                    "PaintLayerTypeRadialGradient",
                    Shape: "circle",
                    Stops:
                    [
                        new(0.0, "#FFADEDFF"),
                        new(0.2, "#FF80DDFF"),
                        new(0.5, "#FFFFFFFF"),
                        new(0.8, "#B98AFFFF"),
                        new(1.0, "#A366FFFF"),
                    ]
                ),
            ],
            [new("#4C00FFFF", 0, 0, 0.1)]
        );

        paint.Should().NotBeNull();
        paint
            .BackgroundImage.Should()
            .Be(
                "radial-gradient(circle, #FFADEDFF 0%, #FF80DDFF 20%, #FFFFFFFF 50%, #B98AFFFF 80%, #A366FFFF 100%)"
            );
        paint.Color.Should().Be("#FFADEDFF");
        paint.TextShadow.Should().Be("0px 0px 0.1px #4C00FFFF");
        paint.IsImageOnly.Should().BeFalse();
    }

    [Fact]
    public void A_real_image_paint_picks_the_animated_webp_at_the_largest_scale()
    {
        // "NNYS 2024" (01JEY00EDNVW20AWX2NPG4HTNF), the paint SoraRiku312 (twitch id 63783703) actually
        // wears — all 24 asset variants captured verbatim from the live v4 API (2026-09-04): 4 scales x
        // {webp, avif, png} static (frameCount 1) plus 4 scales x {webp, avif, gif} animated (frameCount
        // 150). No animated png exists for this paint, matching what 7TV actually ships.
        SevenTvPaintMapper.ImageVariant[] images =
        [
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x_static.webp",
                "image/webp",
                1,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x_static.webp",
                "image/webp",
                2,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x_static.webp",
                "image/webp",
                3,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x_static.webp",
                "image/webp",
                4,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x_static.avif",
                "image/avif",
                1,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x_static.avif",
                "image/avif",
                2,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x_static.avif",
                "image/avif",
                3,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x_static.avif",
                "image/avif",
                4,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x_static.png",
                "image/png",
                1,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x_static.png",
                "image/png",
                2,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x_static.png",
                "image/png",
                3,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x_static.png",
                "image/png",
                4,
                1
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x.webp",
                "image/webp",
                1,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x.webp",
                "image/webp",
                2,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x.webp",
                "image/webp",
                3,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x.webp",
                "image/webp",
                4,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x.avif",
                "image/avif",
                1,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x.avif",
                "image/avif",
                2,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x.avif",
                "image/avif",
                3,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x.avif",
                "image/avif",
                4,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x.gif",
                "image/gif",
                1,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/2x.gif",
                "image/gif",
                2,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/3x.gif",
                "image/gif",
                3,
                150
            ),
            new(
                "https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x.gif",
                "image/gif",
                4,
                150
            ),
        ];

        ChatPaint? paint = SevenTvPaintMapper.Map(
            "01JEY00EDNVW20AWX2NPG4HTNF",
            "NNYS 2024",
            [new("PaintLayerTypeImage", Images: images)],
            []
        );

        paint.Should().NotBeNull();
        // Animated (frameCount 150) beats static; webp beats avif/gif/png; 4x beats 1x-3x.
        paint
            .BackgroundImage.Should()
            .Be(
                "url(\"https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/4x.webp\")"
            );
        // A resolved raster picture is still flagged image-only, so a consumer knows to size it as a
        // texture (background-size: cover) rather than trusting an unscaled gradient sizing default.
        paint.IsImageOnly.Should().BeTrue();
    }

    [Fact]
    public void An_image_only_paint_says_so_rather_than_pretending_it_rendered()
    {
        // 131 of 1,024 live paints are image-only. The asset url is not part of this mapping, so a consumer
        // has to know the colour it got is a stand-in and decide whether that is acceptable on its surface.
        ChatPaint? paint = SevenTvPaintMapper.Map(
            "01JHXJFACX42RV996VE9933TB8",
            "Emerald Doppler",
            [new("PaintLayerTypeImage")],
            [new("#39D21EFF", 0, 0, 0.1)]
        );

        paint.Should().NotBeNull();
        paint.IsImageOnly.Should().BeTrue();
        paint.BackgroundImage.Should().BeNull();
        paint.TextShadow.Should().Be("0px 0px 0.1px #39D21EFF");
    }

    [Fact]
    public void Shadows_are_emitted_in_order_as_one_text_shadow_value()
    {
        ChatPaint? paint = SevenTvPaintMapper.Map(
            "p",
            "n",
            [new("PaintLayerTypeSingleColor", Hex: "#ABCDEFFF")],
            [new("#39D21EFF", 0, 0, 0.1), new("#005557FF", 1, 1, 0.1)]
        );

        paint!.TextShadow.Should().Be("0px 0px 0.1px #39D21EFF, 1px 1px 0.1px #005557FF");
        paint.Color.Should().Be("#ABCDEFFF");
    }

    [Fact]
    public void A_paint_with_nothing_renderable_is_null_rather_than_an_empty_shell()
    {
        // An empty ChatPaint would make every consumer null-check the fields individually, and a widget that
        // forgot would paint a name with "undefined".
        SevenTvPaintMapper.Map("p", "n", [], []).Should().BeNull();
        SevenTvPaintMapper
            .Map("p", "n", [new("PaintLayerTypeUnknownFuture")], [])
            .Should()
            .BeNull();
    }

    [Fact]
    public void An_id_less_paint_is_refused()
    {
        SevenTvPaintMapper.Map("", "n", [Linear(0, false, (0, "#FFFFFFFF"))], []).Should().BeNull();
    }

    [Fact]
    public void Numbers_are_formatted_invariantly_whatever_the_machine_locale_is()
    {
        // On a machine with a comma decimal separator "66,5deg" does not merely look wrong: the comma ends
        // the CSS argument, the browser drops the declaration, and every paint disappears on that machine
        // and nowhere else. Exactly the bug that only shows up on somebody else's computer.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new("nl-NL");

            ChatPaint? paint = SevenTvPaintMapper.Map(
                "p",
                "n",
                [Linear(66.5, repeating: false, (0.575, "#CA804EFF"))],
                [new("#000000FF", 1.5, 0, 0.25)]
            );

            paint!.BackgroundImage.Should().Contain("66.5deg").And.Contain("57.5%");
            paint.TextShadow.Should().Be("1.5px 0px 0.25px #000000FF");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
