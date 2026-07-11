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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// Proves the YouTube send half of the slice-3 seam: a send while live rides the PRIMARY channel's OAuth
/// token into the registered active <c>liveChatId</c> and reports the real outcome; offline (no session)
/// or token-less sends fail honestly with no API call; a reply degrades to a plain send (the Live Chat API
/// has no threading).
/// </summary>
public sealed class YouTubeChatPlatformTests
{
    private static readonly Guid Tenant = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid Primary = Guid.Parse("0199c000-0000-7000-8000-0000000000a2");

    private static (
        YouTubeChatPlatform Platform,
        YouTubeLiveChatSessionRegistry Sessions,
        IYouTubeAccessTokenProvider Tokens,
        IYouTubeLiveChatClient Client
    ) Build(string? token = "bearer-1")
    {
        YouTubeLiveChatSessionRegistry sessions = new();
        IYouTubeAccessTokenProvider tokens = Substitute.For<IYouTubeAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Primary, Arg.Any<CancellationToken>()).Returns(token);
        IYouTubeLiveChatClient client = Substitute.For<IYouTubeLiveChatClient>();
        client
            .SendMessageAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        YouTubeChatPlatform platform = new(
            sessions,
            tokens,
            client,
            NullLogger<YouTubeChatPlatform>.Instance
        );
        return (platform, sessions, tokens, client);
    }

    [Fact]
    public async Task A_send_while_live_rides_the_primary_channels_token_into_the_active_chat()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            _,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");

        bool sent = await platform.SendMessageAsync(Tenant, "hello youtube");

        sent.Should().BeTrue();
        await client
            .Received(1)
            .SendMessageAsync("bearer-1", "chat-42", "hello youtube", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_send_while_offline_fails_honestly_without_any_api_call()
    {
        (YouTubeChatPlatform platform, _, _, IYouTubeLiveChatClient client) = Build();

        bool sent = await platform.SendMessageAsync(Tenant, "hello");

        sent.Should().BeFalse("there is no live chat to write into");
        await client
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default!, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_send_without_a_usable_token_fails_honestly()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            _,
            IYouTubeLiveChatClient client
        ) = Build(token: null);
        sessions.SetLive(Tenant, Primary, "chat-42");

        bool sent = await platform.SendMessageAsync(Tenant, "hello");

        sent.Should().BeFalse();
        await client
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default!, default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_send_reports_false_never_fake_success()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            _,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");
        client
            .SendMessageAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("missing scope", "MISSING_SCOPE"));

        bool sent = await platform.SendMessageAsync(Tenant, "hello");

        sent.Should().BeFalse();
    }

    [Fact]
    public async Task A_reply_degrades_to_a_plain_send_and_the_session_clears_on_offline()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            _,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");

        await platform.SendReplyAsync(Tenant, "parent-msg", "reply text");
        await client
            .Received(1)
            .SendMessageAsync("bearer-1", "chat-42", "reply text", Arg.Any<CancellationToken>());

        sessions.SetOffline(Tenant);
        (await platform.SendMessageAsync(Tenant, "after offline")).Should().BeFalse();
    }
}
