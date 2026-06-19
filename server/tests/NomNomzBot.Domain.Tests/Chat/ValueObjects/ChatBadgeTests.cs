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

namespace NomNomzBot.Domain.Tests.Chat.ValueObjects;

public class ChatBadgeTests
{
    [Fact]
    public void ChatBadge_CreatesWithSetIdAndId()
    {
        ChatBadge badge = new("subscriber", "3");

        badge.SetId.Should().Be("subscriber");
        badge.Id.Should().Be("3");
        badge.Info.Should().BeNull();
    }

    [Fact]
    public void ChatBadge_CreatesWithInfo()
    {
        ChatBadge badge = new("subscriber", "3", "3 months");

        badge.Info.Should().Be("3 months");
    }

    [Fact]
    public void ChatBadge_RecordEquality_SameValues_AreEqual()
    {
        ChatBadge a = new("moderator", "1");
        ChatBadge b = new("moderator", "1");

        a.Should().Be(b);
    }

    [Fact]
    public void ChatBadge_RecordEquality_DifferentSetId_AreNotEqual()
    {
        ChatBadge a = new("moderator", "1");
        ChatBadge b = new("subscriber", "1");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ChatBadge_RecordEquality_DifferentId_AreNotEqual()
    {
        ChatBadge a = new("subscriber", "1");
        ChatBadge b = new("subscriber", "3");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ChatBadge_RecordEquality_InfoDiffers_AreNotEqual()
    {
        ChatBadge a = new("subscriber", "3", "3 months");
        ChatBadge b = new("subscriber", "3", "6 months");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ChatBadge_ToString_ContainsFields()
    {
        ChatBadge badge = new("vip", "1");
        badge.ToString().Should().Contain("vip").And.Contain("1");
    }
}

public class ChatMessageFragmentTests
{
    [Fact]
    public void ChatMessageFragment_DefaultType_IsText()
    {
        ChatMessageFragment fragment = new();
        fragment.Type.Should().Be("text");
    }

    [Fact]
    public void ChatMessageFragment_DefaultText_IsEmpty()
    {
        ChatMessageFragment fragment = new();
        fragment.Text.Should().BeEmpty();
    }

    [Fact]
    public void ChatMessageFragment_TextFragment_SetProperties()
    {
        ChatMessageFragment fragment = new() { Type = "text", Text = "Hello world" };

        fragment.Type.Should().Be("text");
        fragment.Text.Should().Be("Hello world");
    }

    [Fact]
    public void ChatMessageFragment_EmoteFragment_SetsEmoteFields()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "emote",
            Text = "Kappa",
            EmoteId = "25",
            EmoteSetId = "0",
            EmoteOwnerId = "broadcaster123",
            EmoteFormats = ["static", "animated"],
        };

        fragment.Type.Should().Be("emote");
        fragment.EmoteId.Should().Be("25");
        fragment.EmoteSetId.Should().Be("0");
        fragment.EmoteOwnerId.Should().Be("broadcaster123");
        fragment.EmoteFormats.Should().BeEquivalentTo(["static", "animated"]);
    }

    [Fact]
    public void ChatMessageFragment_CheermoteFragment_SetsCheermoteFields()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "cheermote",
            Text = "Cheer100",
            CheermotePrefix = "Cheer",
            CheermoteBits = 100,
            CheermoteTier = 1,
        };

        fragment.Type.Should().Be("cheermote");
        fragment.CheermotePrefix.Should().Be("Cheer");
        fragment.CheermoteBits.Should().Be(100);
        fragment.CheermoteTier.Should().Be(1);
    }

    [Fact]
    public void ChatMessageFragment_MentionFragment_SetsMentionFields()
    {
        ChatMessageFragment fragment = new()
        {
            Type = "mention",
            Text = "@someuser",
            MentionUserId = "uid999",
            MentionUserLogin = "someuser",
            MentionUserName = "SomeUser",
        };

        fragment.Type.Should().Be("mention");
        fragment.MentionUserId.Should().Be("uid999");
        fragment.MentionUserLogin.Should().Be("someuser");
        fragment.MentionUserName.Should().Be("SomeUser");
    }

    [Fact]
    public void ChatMessageFragment_NonEmoteFragment_EmoteFieldsAreNull()
    {
        ChatMessageFragment fragment = new() { Type = "text", Text = "hi" };

        fragment.EmoteId.Should().BeNull();
        fragment.EmoteSetId.Should().BeNull();
        fragment.EmoteFormats.Should().BeEmpty();
    }

    [Fact]
    public void ChatMessageFragment_NonCheermoteFragment_CheermoteFieldsAreNull()
    {
        ChatMessageFragment fragment = new() { Type = "text", Text = "hi" };

        fragment.CheermotePrefix.Should().BeNull();
        fragment.CheermoteBits.Should().BeNull();
        fragment.CheermoteTier.Should().BeNull();
    }
}
