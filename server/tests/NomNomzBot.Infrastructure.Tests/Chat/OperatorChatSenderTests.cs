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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves <see cref="OperatorChatSender"/> sends AS THE LOGGED-IN OPERATOR (chat-client.md §3.1/§3.3): the Helix
/// request rides <see cref="TwitchHelixAuth.Operator"/> keyed by the operator's user id, and its body carries the
/// target channel's Twitch id plus the operator's OWN Twitch id as <c>sender_id</c> — never the bot. Missing
/// channel / missing Twitch identity fail cleanly (typed error, no Twitch call), never a false success.
/// </summary>
public sealed class OperatorChatSenderTests
{
    private static readonly Guid Operator = Guid.Parse("0197b2c0-0000-7000-8000-0000000000a1");
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000b2");

    [Fact]
    public async Task SendAsUserAsync_posts_as_the_operator_with_their_own_twitch_id_as_sender()
    {
        ITwitchHelixTransport transport = Substitute.For<ITwitchHelixTransport>();
        TwitchHelixRequest? sent = null;
        transport
            .SendAsync(Arg.Do<TwitchHelixRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns("channel-777");
        identity
            .GetTwitchUserIdAsync(Operator, Arg.Any<CancellationToken>())
            .Returns("operator-42");

        OperatorChatSender sender = new(
            transport,
            identity,
            NullLogger<OperatorChatSender>.Instance
        );

        Result result = await sender.SendAsUserAsync(Operator, Broadcaster, "hey there", "reply-9");

        result.IsSuccess.Should().BeTrue();

        // The request is the OPERATOR one — the transport resolves the operator's own token from OperatorUserId.
        sent.Should().NotBeNull();
        sent!.Auth.Should().Be(TwitchHelixAuth.Operator);
        sent.OperatorUserId.Should().Be(Operator);
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("chat/messages");

        // The body targets the channel and sends as the operator's OWN Twitch id — never the bot.
        Body(sent, "BroadcasterId").Should().Be("channel-777");
        Body(sent, "SenderId").Should().Be("operator-42");
        Body(sent, "Message").Should().Be("hey there");
        Body(sent, "ReplyParentMessageId").Should().Be("reply-9");
    }

    [Fact]
    public async Task SendAsUserAsync_fails_not_found_and_never_calls_twitch_when_the_channel_is_unknown()
    {
        ITwitchHelixTransport transport = Substitute.For<ITwitchHelixTransport>();
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        OperatorChatSender sender = new(
            transport,
            identity,
            NullLogger<OperatorChatSender>.Instance
        );

        Result result = await sender.SendAsUserAsync(Operator, Broadcaster, "hey there", null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        await transport
            .DidNotReceive()
            .SendAsync(Arg.Any<TwitchHelixRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsUserAsync_fails_no_token_when_the_operator_has_no_linked_twitch_identity()
    {
        ITwitchHelixTransport transport = Substitute.For<ITwitchHelixTransport>();
        ITwitchIdentityResolver identity = Substitute.For<ITwitchIdentityResolver>();
        identity
            .GetTwitchChannelIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns("channel-777");
        identity
            .GetTwitchUserIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        OperatorChatSender sender = new(
            transport,
            identity,
            NullLogger<OperatorChatSender>.Instance
        );

        Result result = await sender.SendAsUserAsync(Operator, Broadcaster, "hey there", null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NoToken);
        await transport
            .DidNotReceive()
            .SendAsync(Arg.Any<TwitchHelixRequest>(), Arg.Any<CancellationToken>());
    }

    private static string? Body(TwitchHelixRequest request, string property) =>
        request.Body!.GetType().GetProperty(property)!.GetValue(request.Body)?.ToString();
}
