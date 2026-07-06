// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the scale-suffixed image-url fields on the chat-assets DTOs deserialize from Twitch's real wire shape.
/// The transport's naming policy is <c>SnakeCaseLower</c>, which renders <c>ImageUrl1x</c> as <c>image_url1x</c>
/// (no underscore before the scale) — so without the explicit <c>[JsonPropertyName]</c> the badge / emote image
/// urls silently deserialize to null and render blank. These tests mirror that policy and assert the mapping.
/// </summary>
public sealed class TwitchChatAssetsDtoTests
{
    // Mirrors TwitchHelixTransport.WireJson so the assertion runs against the ACTUAL policy Twitch responses use.
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Badge_image_urls_map_from_the_underscored_scale_suffix()
    {
        const string json = """
            {
              "set_id": "moderator",
              "versions": [
                {
                  "id": "1",
                  "image_url_1x": "https://badge/1",
                  "image_url_2x": "https://badge/2",
                  "image_url_4x": "https://badge/4",
                  "title": "Moderator",
                  "description": "Moderator",
                  "click_action": "",
                  "click_url": ""
                }
              ]
            }
            """;

        TwitchChatBadgeSet? set = JsonSerializer.Deserialize<TwitchChatBadgeSet>(json, Wire);

        set!.SetId.Should().Be("moderator");
        TwitchChatBadgeVersion version = set.Versions.Should().ContainSingle().Subject;
        version.ImageUrl1x.Should().Be("https://badge/1");
        version.ImageUrl2x.Should().Be("https://badge/2");
        version.ImageUrl4x.Should().Be("https://badge/4");
    }

    [Fact]
    public void Emote_image_urls_map_from_the_underscored_scale_suffix()
    {
        const string json = """
            {
              "id": "emotesv2_1",
              "name": "Kappa",
              "images": {
                "url_1x": "https://emote/1",
                "url_2x": "https://emote/2",
                "url_4x": "https://emote/4"
              },
              "format": ["static"],
              "scale": ["1.0"],
              "theme_mode": ["light"]
            }
            """;

        TwitchGlobalEmote? emote = JsonSerializer.Deserialize<TwitchGlobalEmote>(json, Wire);

        emote!.Name.Should().Be("Kappa");
        emote.Images.Url1x.Should().Be("https://emote/1");
        emote.Images.Url2x.Should().Be("https://emote/2");
        emote.Images.Url4x.Should().Be("https://emote/4");
    }
}
