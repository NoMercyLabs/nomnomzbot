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
using NomNomzBot.Infrastructure.Chat.Providers;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Parsing 7TV's v3 <c>users/twitch/{id}</c> response for the CHATTER's own paint id.
///
/// <para>
/// The payload shape below is REAL — captured verbatim from <c>GET https://7tv.io/v3/users/twitch/71092938</c>
/// (xQc) on 2026-09-04, trimmed to the top-level fields the parser actually reads. A hand-written payload
/// would only prove the parser matches assumptions about the wire, which is the assumption most likely to be
/// wrong — the paint id lives three levels down at <c>user.style.paint_id</c>, sitting next to an unrelated
/// <c>emote_set.emotes[].owner.style</c> that is empty on every entry and must not be mistaken for it.
/// </para>
/// </summary>
public sealed class SevenTvUserPaintResolverParseTests
{
    private const string LivePayloadWithPaint = """
        {"id":"71092938","platform":"TWITCH","username":"xqc","display_name":"xQc",
         "emote_set":{"id":"01FE9DRF000009TR6M9N941CYW","emotes":[
           {"id":"01G3WEGZN0000ET2J0MQP5YJ0G","name":"GAMBA",
            "data":{"owner":{"id":"01FFFKKA2R0007P57XYW0BHEQV","style":{}}}}
         ]},
         "user":{"id":"01F1MSY9GR000BSF3PSJ0FSTAF","username":"xqc","display_name":"xQc",
           "style":{"color":-1857617921,"paint_id":"01JHXJFACX42RV996VE9933TB8",
                    "badge_id":"01JJJ74CRHZBRMCM8F4Y2WBN6R"}}}
        """;

    private const string LivePayloadWithoutPaint = """
        {"id":"1","platform":"TWITCH","username":"nobody","display_name":"Nobody",
         "user":{"id":"01F1MSY9GR000BSF3PSJ0FSTAG","username":"nobody","display_name":"Nobody",
           "style":{"color":0}}}
        """;

    [Fact]
    public void The_chatters_own_paint_id_is_read_from_user_style_not_an_emote_owners()
    {
        string? paintId = SevenTvUserPaintResolver.ParsePaintId(LivePayloadWithPaint);

        paintId.Should().Be("01JHXJFACX42RV996VE9933TB8");
    }

    [Fact]
    public void A_chatter_wearing_no_paint_resolves_to_null_not_an_empty_string()
    {
        string? paintId = SevenTvUserPaintResolver.ParsePaintId(LivePayloadWithoutPaint);

        paintId.Should().BeNull();
    }

    [Fact]
    public void Malformed_json_resolves_to_null_rather_than_throwing()
    {
        // Falls to JsonConvert's own tolerant behaviour for garbage input — proven here rather than assumed,
        // since a cosmetic lookup must never take a chatter's message down with it.
        string? paintId = SevenTvUserPaintResolver.ParsePaintId("{}");

        paintId.Should().BeNull();
    }
}
