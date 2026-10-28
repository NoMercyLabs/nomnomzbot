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
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves <see cref="ChatFragmentMapper"/> — the single mapping shared by the live <c>DashboardHub</c> broadcast
/// (<c>ChatMessageBroadcastHandler</c>) and the REST chat-history page (<c>ChatController.GetMessages</c>) — carries
/// every decorated field (chat-decoration spec §4) onto the wire DTO, so both surfaces render the identical shape
/// for the identical decorated message.
/// </summary>
public sealed class ChatFragmentMapperTests
{
    [Fact]
    public void MapFragment_carries_the_resolved_emote_fields()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = "PepeLaugh",
            Emote = new(
                EmoteProvider.SevenTv,
                "7tv-1",
                "PepeLaugh",
                new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x" },
                Animated: true,
                ZeroWidth: true
            ),
        };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.Type.Should().Be("emote");
        dto.Text.Should().Be("PepeLaugh");
        dto.Emote.Should().NotBeNull();
        dto.Emote!.Id.Should().Be("7tv-1");
        dto.Emote.Provider.Should().Be("SevenTv");
        dto.Emote.Format.Should().Be("animated");
        dto.Emote.ZeroWidth.Should().BeTrue();
        dto.Emote.Urls["1"].Should().Be("https://cdn.7tv/1x");
        dto.Cheermote.Should().BeNull();
        dto.Mention.Should().BeNull();
        dto.LinkPreview.Should().BeNull();
    }

    [Fact]
    public void MapFragment_carries_the_emote_static_urls_for_reduced_motion()
    {
        ChatMessageFragment animated = new()
        {
            Type = "emote",
            Text = "catJAM",
            Emote = new(
                EmoteProvider.SevenTv,
                "7tv-2",
                "catJAM",
                new Dictionary<string, string> { ["1"] = "https://cdn.7tv/1x.webp" },
                Animated: true,
                ZeroWidth: false,
                StaticUrls: new Dictionary<string, string>
                {
                    ["1"] = "https://cdn.7tv/1x_static.webp",
                }
            ),
        };
        ChatMessageFragment cachedBeforeTheField = new()
        {
            Type = "emote",
            Text = "Old",
            Emote = new(
                EmoteProvider.Bttv,
                "b1",
                "Old",
                new Dictionary<string, string> { ["1"] = "https://cdn.btt/1x" },
                Animated: false,
                ZeroWidth: false
            ),
        };

        ChatEmoteDto withStatic = ChatFragmentMapper.MapFragment(animated).Emote!;
        ChatEmoteDto fallback = ChatFragmentMapper.MapFragment(cachedBeforeTheField).Emote!;

        withStatic.Urls["1"].Should().Be("https://cdn.7tv/1x.webp");
        withStatic.StaticUrls!["1"].Should().Be("https://cdn.7tv/1x_static.webp");
        fallback.StaticUrls.Should().BeEquivalentTo(fallback.Urls);
    }

    [Fact]
    public void MapFragment_carries_a_twitch_emotes_code_type_tier_and_owner()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = "streamerSub",
            Emote = new(
                EmoteProvider.Twitch,
                "300",
                "streamerSub",
                new Dictionary<string, string>(),
                Animated: false,
                ZeroWidth: false,
                Type: "subscriptions",
                Tier: 2,
                Owner: new("100", "streamer", "Streamer", "https://www.twitch.tv/streamer")
            ),
        };

        ChatEmoteDto emote = ChatFragmentMapper.MapFragment(fragment).Emote!;

        emote.Code.Should().Be("streamerSub");
        emote.Type.Should().Be("subscriptions");
        emote.Tier.Should().Be(2);
        emote
            .Owner.Should()
            .Be(
                new ChatEmoteOwnerDto(
                    "100",
                    "streamer",
                    "Streamer",
                    "https://www.twitch.tv/streamer"
                )
            );
    }

    [Fact]
    public void MapFragment_carries_the_emote_artist()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = "ArtEmote",
            Emote = new(
                EmoteProvider.Ffz,
                "78",
                "ArtEmote",
                new Dictionary<string, string>(),
                Animated: false,
                ZeroWidth: false,
                Artist: new("9", "drawer", "Drawer", "https://www.frankerfacez.com/drawer")
            ),
        };

        ChatEmoteDto emote = ChatFragmentMapper.MapFragment(fragment).Emote!;

        emote
            .Artist.Should()
            .Be(
                new ChatEmoteOwnerDto(
                    "9",
                    "drawer",
                    "Drawer",
                    "https://www.frankerfacez.com/drawer"
                )
            );
    }

    [Fact]
    public void MapFragment_leaves_the_details_null_when_the_emote_has_none()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = "Kappa",
            Emote = new(
                EmoteProvider.Twitch,
                "25",
                "Kappa",
                new Dictionary<string, string>(),
                Animated: false,
                ZeroWidth: false
            ),
        };

        ChatEmoteDto emote = ChatFragmentMapper.MapFragment(fragment).Emote!;

        emote.Code.Should().Be("Kappa");
        emote.Type.Should().BeNull();
        emote.Tier.Should().BeNull();
        emote.Owner.Should().BeNull();
    }

    [Fact]
    public void MapFragment_carries_the_resolved_cheermote_image()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "cheermote",
            Text = "Cheer100",
            CheermotePrefix = "Cheer",
            CheermoteBits = 100,
            CheermoteTier = 1,
            CheermoteImage = new(
                new Dictionary<string, string> { ["1"] = "https://cdn/cheer1.gif" },
                Animated: true,
                ColorHex: "#979797"
            ),
        };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.Cheermote.Should().NotBeNull();
        dto.Cheermote!.Prefix.Should().Be("Cheer");
        dto.Cheermote.Bits.Should().Be(100);
        dto.Cheermote.Tier.Should().Be(1);
        dto.Cheermote.Animated.Should().BeTrue();
        dto.Cheermote.ColorHex.Should().Be("#979797");
        dto.Cheermote.Urls!["1"].Should().Be("https://cdn/cheer1.gif");
    }

    [Fact]
    public void MapFragment_carries_the_mention_s_resolved_color()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "mention",
            Text = "@Stoney_Eagle",
            MentionUserId = "u1",
            MentionUserLogin = "stoney_eagle",
            MentionUserName = "Stoney_Eagle",
            MentionColorHex = "#FF69B4",
        };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.Mention.Should().NotBeNull();
        dto.Mention!.UserId.Should().Be("u1");
        dto.Mention.Username.Should().Be("stoney_eagle");
        dto.Mention.DisplayName.Should().Be("Stoney_Eagle");
        dto.Mention.Color.Should().Be("#FF69B4");
    }

    [Fact]
    public void MapFragment_carries_the_link_preview()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "link",
            Text = "https://example.com",
            LinkUrl = "https://example.com",
            LinkPreview = new(
                "example.com",
                "Example Domain",
                "An example",
                "https://example.com/og.png"
            ),
        };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.LinkUrl.Should().Be("https://example.com");
        dto.LinkPreview.Should().NotBeNull();
        dto.LinkPreview!.Host.Should().Be("example.com");
        dto.LinkPreview.Title.Should().Be("Example Domain");
        dto.LinkPreview.ImageUrl.Should().Be("https://example.com/og.png");
    }

    [Fact]
    public void MapFragment_leaves_a_plain_text_fragment_with_every_optional_field_null()
    {
        ChatMessageFragment fragment = new() { Type = "text", Text = "hello" };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.Type.Should().Be("text");
        dto.Text.Should().Be("hello");
        dto.Emote.Should().BeNull();
        dto.Cheermote.Should().BeNull();
        dto.Mention.Should().BeNull();
        dto.LinkUrl.Should().BeNull();
        dto.LinkPreview.Should().BeNull();
        dto.Gif.Should().BeNull();
    }

    [Fact]
    public void MapFragment_carries_the_native_gif_s_real_url_not_a_placeholder()
    {
        // Regression for the priority bug reported 2026-09-01: Twitch's native chat GIF fragment (GIPHY-backed,
        // Tier 2+ subscriber feature) already carries a real, directly-fetchable url — the mapper must carry
        // it straight onto the wire DTO, never drop it or substitute anything else.
        ChatMessageFragment fragment = new()
        {
            Type = "gif",
            Text = "Cat Festival GIF by W&W",
            GifId = "abc123",
            GifUrl = "https://media.giphy.com/media/abc123/giphy.gif",
        };

        ChatFragmentDto dto = ChatFragmentMapper.MapFragment(fragment);

        dto.Type.Should().Be("gif");
        dto.Text.Should().Be("Cat Festival GIF by W&W");
        dto.Gif.Should().NotBeNull();
        dto.Gif!.GifId.Should().Be("abc123");
        dto.Gif.Url.Should().Be("https://media.giphy.com/media/abc123/giphy.gif");
    }

    [Fact]
    public void MapBadge_carries_the_resolved_image_urls()
    {
        ResolvedChatBadge badge = new(
            "subscriber",
            "6",
            "6",
            new Dictionary<string, string> { ["4"] = "https://cdn/sub-tier6.png" }
        );

        ChatBadgeDto dto = ChatFragmentMapper.MapBadge(badge);

        dto.SetId.Should().Be("subscriber");
        dto.Id.Should().Be("6");
        dto.Info.Should().Be("6");
        dto.Urls["4"].Should().Be("https://cdn/sub-tier6.png");
    }
}
