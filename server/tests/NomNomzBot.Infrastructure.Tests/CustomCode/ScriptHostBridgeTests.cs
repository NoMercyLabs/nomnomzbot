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
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Infrastructure.CustomCode;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the per-execution host bridge (custom-code.md §3.1/§6.2): the granted chat.send capability dispatches to
/// the channel's chat transport with the host-resolved Twitch channel id (the guest only ever holds the Guid); a
/// granted-but-unwired capability resolves to a no-op.
/// </summary>
public sealed class ScriptHostBridgeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");

    [Fact]
    public async Task Chat_send_dispatches_to_the_chat_transport_with_the_resolved_channel()
    {
        ITwitchChatService chat = Substitute.For<ITwitchChatService>();
        ITwitchIdentityResolver resolver = Substitute.For<ITwitchIdentityResolver>();
        resolver.GetTwitchChannelIdAsync(Channel, Arg.Any<CancellationToken>()).Returns("chan123");
        ScriptHostBridge bridge = new(Channel, chat, resolver);

        string? result = bridge.Resolve("chat.send")(
            "chat.send",
            ["hello world"],
            CancellationToken.None
        );

        result.Should().BeNull();
        await chat.Received()
            .SendMessageAsync("chan123", "hello world", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_granted_but_unwired_capability_is_a_noop()
    {
        ScriptHostBridge bridge = new(
            Channel,
            Substitute.For<ITwitchChatService>(),
            Substitute.For<ITwitchIdentityResolver>()
        );

        bridge
            .Resolve("music.queue")("music.queue", ["a song"], CancellationToken.None)
            .Should()
            .BeNull();
    }
}
