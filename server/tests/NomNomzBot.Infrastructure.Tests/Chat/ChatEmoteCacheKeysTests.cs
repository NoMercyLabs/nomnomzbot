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
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

// Stoney 2026-10-14: "all the channel emotes are stuffed together as twitch global". The build before had cached
// emote lists without an owner; the new build read them and tagged every emote global.
public sealed class ChatEmoteCacheKeysTests
{
    private sealed record Before(string Id, string Code);

    private sealed record After(string Id, string Code, string? OwnerId);

    [Fact]
    public void A_record_that_gains_a_field_gets_a_different_shape()
    {
        ChatEmoteCacheKeys
            .ShapeOf(typeof(Before))
            .Should()
            .NotBe(ChatEmoteCacheKeys.ShapeOf(typeof(After)));
    }

    [Fact]
    public void The_same_record_keeps_its_shape()
    {
        ChatEmoteCacheKeys
            .ShapeOf(typeof(After))
            .Should()
            .Be(ChatEmoteCacheKeys.ShapeOf(typeof(After)));
    }

    [Fact]
    public void Every_emote_list_key_carries_the_emote_shape()
    {
        string shape = ChatEmoteCacheKeys.EmoteShape;

        ChatEmoteCacheKeys.Global(EmoteProvider.Twitch).Should().Contain(shape);
        ChatEmoteCacheKeys.Channel(EmoteProvider.SevenTv, "1").Should().Contain(shape);
        ChatEmoteCacheKeys.TwitchUser("1").Should().Contain(shape);
    }
}
