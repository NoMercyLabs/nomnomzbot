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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

/// <summary>
/// <c>{chat.message.noCommand}</c>: the trigger message without its command word. The command word is the first
/// word, when it starts with the channel's command prefix (<c>!</c> in the fixture channel).
/// </summary>
public sealed class ChatMessageNoCommandTests
{
    [Theory]
    [InlineData("!hug hello big world", "hello big world")]
    [InlineData("!hug    spaced   out", "spaced   out")]
    [InlineData("!hug", "")]
    [InlineData("!hug ", "")]
    [InlineData("hello big world", "hello big world")]
    [InlineData("?hug hello", "?hug hello")]
    [InlineData("say !hug now", "say !hug now")]
    public async Task The_command_word_is_dropped_only_when_it_starts_with_the_channel_prefix(
        string message,
        string expected
    )
    {
        await using ChatHelperFixture fixture = new();
        Dictionary<string, string> seeds = ChatHelperFixture.Seeds();
        seeds["message"] = message;

        string rendered = await fixture.RenderAsync("[{chat.message.noCommand}]", seeds);

        rendered.Should().Be($"[{expected}]");
    }

    [Fact]
    public async Task A_render_without_a_message_seed_leaves_the_token_as_written()
    {
        await using ChatHelperFixture fixture = new();
        Dictionary<string, string> seeds = ChatHelperFixture.Seeds();
        seeds.Remove("message");

        string rendered = await fixture.RenderAsync("[{chat.message.noCommand}]", seeds);

        rendered.Should().Be("[{chat.message.noCommand}]");
    }
}
