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
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Parsing 7TV's v4 GraphQL paint payload.
///
/// <para>
/// The JSON below is REAL — three paints captured verbatim from the live endpoint on 2026-09-04, one per
/// layer kind that actually occurs. A hand-written payload would only prove the parser matches my
/// assumptions about the wire, which is the assumption most likely to be wrong.
/// </para>
/// </summary>
public sealed class SevenTvPaintCatalogueParseTests
{
    private const string LivePayload = """
        {"data":{"paints":{"paints":[
         {"id":"01GHA42XER0001KT343YQ6DM2E","name":"Ginger Tabby S","data":{
           "layers":[{"ty":{"__typename":"PaintLayerTypeLinearGradient","angle":66,"repeating":true,
             "stops":[{"at":0.57,"color":{"hex":"#CA804EFF"}},{"at":0.65,"color":{"hex":"#503611FF"}},
                      {"at":1.0,"color":{"hex":"#C89041FF"}}]}}],
           "shadows":[{"color":{"hex":"#000000FF"},"offsetX":0.0,"offsetY":0.0,"blur":0.5}]}},
         {"id":"01HQE7KJWG0000D7GK7EKRAPN3","name":"Flowerchild oA","data":{
           "layers":[{"ty":{"__typename":"PaintLayerTypeRadialGradient","repeating":false,"shape":"ELLIPSE",
             "stops":[{"at":0.0,"color":{"hex":"#FFB6C1FF"}},{"at":1.0,"color":{"hex":"#FF69B4FF"}}]}}],
           "shadows":[]}},
         {"id":"01FQB6K5T0000BDD0YMN21KEXX","name":"Staff Shine","data":{
           "layers":[{"ty":{"__typename":"PaintLayerTypeImage"}}],
           "shadows":[]}}
        ]}}}
        """;

    [Fact]
    public void Every_paint_in_a_real_payload_is_read()
    {
        IReadOnlyDictionary<string, ChatPaint> paints = SevenTvPaintCatalogue.Parse(LivePayload);

        paints.Should().HaveCount(3);
        paints
            .Keys.Should()
            .Contain([
                "01GHA42XER0001KT343YQ6DM2E",
                "01HQE7KJWG0000D7GK7EKRAPN3",
                "01FQB6K5T0000BDD0YMN21KEXX",
            ]);
    }

    [Fact]
    public void A_linear_gradient_paint_arrives_as_the_css_a_browser_can_use()
    {
        ChatPaint paint = SevenTvPaintCatalogue.Parse(LivePayload)["01GHA42XER0001KT343YQ6DM2E"];

        paint.Name.Should().Be("Ginger Tabby S");
        paint
            .BackgroundImage.Should()
            .Be("repeating-linear-gradient(66deg, #CA804EFF 57%, #503611FF 65%, #C89041FF 100%)");
        paint.TextShadow.Should().Be("0px 0px 0.5px #000000FF");
    }

    [Fact]
    public void A_shape_is_lower_cased_before_it_reaches_css()
    {
        // 7TV sends "ELLIPSE"; CSS only knows "ellipse". Passing the wire value through unchanged makes the
        // browser drop the whole declaration, so the paint disappears instead of degrading.
        ChatPaint paint = SevenTvPaintCatalogue.Parse(LivePayload)["01HQE7KJWG0000D7GK7EKRAPN3"];

        paint.BackgroundImage.Should().Be("radial-gradient(ellipse, #FFB6C1FF 0%, #FF69B4FF 100%)");
    }

    [Fact]
    public void An_image_paint_is_kept_and_flagged_rather_than_dropped()
    {
        // 131 of the 1,024 live paints are image-only. Dropping them would leave those chatters with no
        // cosmetic at all and no way to tell that from "has no paint".
        ChatPaint paint = SevenTvPaintCatalogue.Parse(LivePayload)["01FQB6K5T0000BDD0YMN21KEXX"];

        paint.IsImageOnly.Should().BeTrue();
        paint.BackgroundImage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"errors":[{"message":"boom"}]}""")]
    [InlineData("""{"data":{"paints":{"paints":"not-an-array"}}}""")]
    public void A_payload_that_is_not_what_we_expect_yields_no_paints_instead_of_throwing(
        string json
    )
    {
        // 7TV answering with an error envelope, or changing shape, must cost chatters their cosmetic and
        // nothing more — never their message, and never the chat pipeline.
        Action parse = () => SevenTvPaintCatalogue.Parse(json);

        if (json.Length == 0)
        {
            parse
                .Should()
                .Throw<Exception>("an empty body is not JSON at all and the caller catches it");
            return;
        }

        parse.Should().NotThrow();
        SevenTvPaintCatalogue.Parse(json).Should().BeEmpty();
    }

    [Fact]
    public void A_single_malformed_paint_does_not_take_the_rest_of_the_catalogue_with_it()
    {
        const string mixed = """
            {"data":{"paints":{"paints":[
             {"id":"broken","name":"Broken","data":{"layers":[{"ty":{}}],"shadows":[]}},
             {"id":"good","name":"Good","data":{
               "layers":[{"ty":{"__typename":"PaintLayerTypeLinearGradient","angle":0,"repeating":false,
                 "stops":[{"at":0,"color":{"hex":"#FFFFFFFF"}}]}}],"shadows":[]}}
            ]}}}
            """;

        IReadOnlyDictionary<string, ChatPaint> paints = SevenTvPaintCatalogue.Parse(mixed);

        paints.Should().ContainKey("good");
        paints.Should().NotContainKey("broken", "a layer with no type renders nothing");
    }

    [Fact]
    public void An_image_layer_arrives_with_its_asset_variants_and_the_animated_one_wins()
    {
        // "NNYS 2024" (01JEY00EDNVW20AWX2NPG4HTNF), the paint SoraRiku312 (twitch id 63783703) actually
        // wears — trimmed to two of its real asset variants (a static webp and the matching animated webp),
        // captured verbatim from the live v4 API with the PaintLayerTypeImage fragment added to the query
        // (2026-09-04). Proves the catalogue's own JSON walk reads "images", not just the mapper's unit
        // tests against a hand-built Layer.
        const string payload = """
            {"data":{"paints":{"paints":[
             {"id":"01JEY00EDNVW20AWX2NPG4HTNF","name":"NNYS 2024","data":{
               "layers":[{"ty":{"__typename":"PaintLayerTypeImage","images":[
                 {"url":"https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x_static.webp",
                  "mime":"image/webp","scale":1,"frameCount":1},
                 {"url":"https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x.webp",
                  "mime":"image/webp","scale":1,"frameCount":150}]}}],
               "shadows":[{"color":{"hex":"#1A71FFFF"},"offsetX":0.0,"offsetY":0.0,"blur":0.1}]}}
            ]}}}
            """;

        ChatPaint paint = SevenTvPaintCatalogue.Parse(payload)["01JEY00EDNVW20AWX2NPG4HTNF"];

        // The animated variant (frameCount 150) beats the static one (frameCount 1) at the same scale/mime.
        paint
            .BackgroundImage.Should()
            .Be(
                "url(\"https://cdn.7tv.app/paint/01JEY00EDNVW20AWX2NPG4HTNF/layer/01JH1Q77D54RJ8DKK9M5WCYR27/1x.webp\")"
            );
        paint.IsImageOnly.Should().BeTrue();
    }

    [Fact]
    public void A_paint_with_no_layers_and_no_shadows_produces_no_entry_at_all()
    {
        // "Breathing" (01GE2QA6V0000BGYWMCXGPGCTQ) — one of the 7 live paints with zero layers, captured
        // verbatim (2026-09-04); unlike its zero-layer siblings it ALSO has zero shadows, so there is
        // nothing whatsoever to render. It must not appear in the catalogue at all — same as a chatter
        // wearing no paint, not an entry with every field null.
        const string payload = """
            {"data":{"paints":{"paints":[
             {"id":"01GE2QA6V0000BGYWMCXGPGCTQ","name":"Breathing","data":{"layers":[],"shadows":[]}}
            ]}}}
            """;

        IReadOnlyDictionary<string, ChatPaint> paints = SevenTvPaintCatalogue.Parse(payload);

        paints.Should().BeEmpty();
    }
}
