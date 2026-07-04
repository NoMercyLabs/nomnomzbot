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
            Emote = new ChatEmote(
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
    public void MapFragment_carries_the_resolved_cheermote_image()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "cheermote",
            Text = "Cheer100",
            CheermotePrefix = "Cheer",
            CheermoteBits = 100,
            CheermoteTier = 1,
            CheermoteImage = new CheermoteImage(
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
            LinkPreview = new LinkPreview(
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
