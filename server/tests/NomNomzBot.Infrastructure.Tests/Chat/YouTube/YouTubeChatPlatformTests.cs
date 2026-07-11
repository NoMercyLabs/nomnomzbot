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
/// has no threading). Moderation bookkeeping: every issued ban ledgers its insert-returned id, and unban
/// consumes the ledger — token-only, so it works offline — or no-ops honestly when nothing is recorded.
/// </summary>
public sealed class YouTubeChatPlatformTests
{
    private static readonly Guid Tenant = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid Primary = Guid.Parse("0199c000-0000-7000-8000-0000000000a2");

    private static (
        YouTubeChatPlatform Platform,
        YouTubeLiveChatSessionRegistry Sessions,
        IYouTubeLiveChatBanLedger Bans,
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
        IYouTubeLiveChatBanLedger bans = Substitute.For<IYouTubeLiveChatBanLedger>();

        YouTubeChatPlatform platform = new(
            sessions,
            tokens,
            client,
            bans,
            NullLogger<YouTubeChatPlatform>.Instance
        );
        return (platform, sessions, bans, client);
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
    public async Task A_timeout_is_a_temporary_ban_and_a_ban_is_permanent_and_both_ledger_their_ban_id()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatBanLedger bans,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");
        client
            .BanUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("ban-9"));

        await platform.TimeoutUserAsync(Tenant, "UCbad", 600, "spam");
        await platform.BanUserAsync(Tenant, "UCworse", "worse spam");

        await client
            .Received(1)
            .BanUserAsync("bearer-1", "chat-42", "UCbad", 600, Arg.Any<CancellationToken>());
        await client
            .Received(1)
            .BanUserAsync("bearer-1", "chat-42", "UCworse", null, Arg.Any<CancellationToken>());

        // The insert-returned ban id is the ONLY key liveChatBans.delete accepts — both moderation verbs
        // must ledger it (with the issuing PRIMARY channel, so an offline unban can resolve the token).
        await bans.Received(1)
            .RecordAsync(
                Tenant,
                Primary,
                "chat-42",
                "UCbad",
                "ban-9",
                600,
                Arg.Any<CancellationToken>()
            );
        await bans.Received(1)
            .RecordAsync(
                Tenant,
                Primary,
                "chat-42",
                "UCworse",
                "ban-9",
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_failed_ban_ledgers_nothing()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatBanLedger bans,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");
        client
            .BanUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<string>("missing scope", "MISSING_SCOPE"));

        await platform.BanUserAsync(Tenant, "UCbad");

        // A ledgered id that YouTube never created would make a later unban lie about lifting a ban.
        await bans.DidNotReceiveWithAnyArgs()
            .RecordAsync(
                default,
                default,
                default!,
                default!,
                default!,
                default,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unban_consumes_the_ledger_and_deletes_by_ban_id_even_offline()
    {
        // OFFLINE on purpose (no SetLive): a permanent ban outlives the live session, so the unban resolves
        // the token from the ledgered PRIMARY channel — no active chat required.
        (
            YouTubeChatPlatform platform,
            _,
            IYouTubeLiveChatBanLedger bans,
            IYouTubeLiveChatClient client
        ) = Build();
        bans.ConsumeLatestAsync(Tenant, "UCbad", Arg.Any<CancellationToken>())
            .Returns(new YouTubeConsumedBan("ban-9", Primary));
        client
            .UnbanUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await platform.UnbanUserAsync(Tenant, "UCbad");

        await client.Received(1).UnbanUserAsync("bearer-1", "ban-9", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unban_with_no_recorded_ban_is_an_honest_no_op()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            IYouTubeLiveChatBanLedger bans,
            IYouTubeLiveChatClient client
        ) = Build();
        sessions.SetLive(Tenant, Primary, "chat-42");
        bans.ConsumeLatestAsync(Tenant, "UCnever", Arg.Any<CancellationToken>())
            .Returns((YouTubeConsumedBan?)null);

        await platform.UnbanUserAsync(Tenant, "UCnever");

        await client
            .DidNotReceiveWithAnyArgs()
            .UnbanUserAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_message_delete_rides_the_primary_token_and_offline_moderation_is_a_no_op()
    {
        (
            YouTubeChatPlatform platform,
            YouTubeLiveChatSessionRegistry sessions,
            _,
            IYouTubeLiveChatClient client
        ) = Build();
        client
            .DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Offline: nothing to moderate — no API call.
        await platform.DeleteMessageAsync(Tenant, "m-1");
        await client
            .DidNotReceiveWithAnyArgs()
            .DeleteMessageAsync(default!, default!, Arg.Any<CancellationToken>());

        sessions.SetLive(Tenant, Primary, "chat-42");
        await platform.DeleteMessageAsync(Tenant, "m-1");
        await client
            .Received(1)
            .DeleteMessageAsync("bearer-1", "m-1", Arg.Any<CancellationToken>());
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
