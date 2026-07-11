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
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Infrastructure.Chat.Kick;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick half of the chat seam: sends and moderation ride the tenant's vaulted token with the
/// numeric broadcaster id the provider resolved; a reply threads NATIVELY (reply id forwarded, not
/// degraded); the seam's seconds convert to Kick's minutes (ceiling, 1–10080 clamp); a token-less tenant
/// or a non-numeric target is an honest no-op with no API call.
/// </summary>
public sealed class KickChatPlatformTests
{
    private static readonly Guid Tenant = Guid.Parse("0192c000-0000-7000-8000-0000000000a1");
    private const long KickId = 12345;

    private static (KickChatPlatform Platform, IKickApiClient Client) Build(bool withToken = true)
    {
        IKickAccessTokenProvider tokens = Substitute.For<IKickAccessTokenProvider>();
        tokens
            .GetAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(withToken ? new KickAccess("kick-bearer-1", KickId) : null);
        IKickApiClient client = Substitute.For<IKickApiClient>();
        client
            .SendMessageAsync(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success("m-1"));
        client
            .TimeoutUserAsync(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        client
            .UnbanUserAsync(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        KickChatPlatform platform = new(tokens, client, NullLogger<KickChatPlatform>.Instance);
        return (platform, client);
    }

    [Fact]
    public async Task A_send_rides_the_resolved_token_and_broadcaster_id()
    {
        (KickChatPlatform platform, IKickApiClient client) = Build();

        bool sent = await platform.SendMessageAsync(Tenant, "hello kick");

        sent.Should().BeTrue();
        await client
            .Received(1)
            .SendMessageAsync(
                "kick-bearer-1",
                KickId,
                "hello kick",
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_reply_threads_natively_with_the_parent_message_id()
    {
        (KickChatPlatform platform, IKickApiClient client) = Build();

        await platform.SendReplyAsync(Tenant, "parent-9", "threaded reply");

        await client
            .Received(1)
            .SendMessageAsync(
                "kick-bearer-1",
                KickId,
                "threaded reply",
                "parent-9",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_tenant_without_a_usable_token_fails_honestly_with_no_api_call()
    {
        (KickChatPlatform platform, IKickApiClient client) = Build(withToken: false);

        bool sent = await platform.SendMessageAsync(Tenant, "hello");

        sent.Should().BeFalse();
        await client
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default!, default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_timeout_converts_the_seams_seconds_to_kick_minutes()
    {
        (KickChatPlatform platform, IKickApiClient client) = Build();

        await platform.TimeoutUserAsync(Tenant, "678", durationSeconds: 90, "spam");

        // 90s rounds UP to 2 minutes — Kick's floor is 1 minute and truncation would under-punish.
        await client
            .Received(1)
            .TimeoutUserAsync(
                "kick-bearer-1",
                KickId,
                678,
                2,
                "spam",
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(1, 1)] // sub-minute rounds up to Kick's floor
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(600, 10)]
    [InlineData(int.MaxValue, 10080)] // clamped to Kick's 7-day max
    public void Seconds_to_kick_minutes_is_ceiling_clamped(int seconds, int expectedMinutes)
    {
        KickChatPlatform.ToKickMinutes(seconds).Should().Be(expectedMinutes);
    }

    [Fact]
    public async Task An_unban_is_direct_by_user_id_no_ledger()
    {
        (KickChatPlatform platform, IKickApiClient client) = Build();

        await platform.UnbanUserAsync(Tenant, "678");

        await client
            .Received(1)
            .UnbanUserAsync("kick-bearer-1", KickId, 678, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_non_numeric_target_is_an_honest_no_op()
    {
        // Kick user ids are integers — a foreign-platform id must not turn into a garbage API call.
        (KickChatPlatform platform, IKickApiClient client) = Build();

        await platform.BanUserAsync(Tenant, "not-a-number");

        await client
            .DidNotReceiveWithAnyArgs()
            .BanUserAsync(default!, default, default, default, Arg.Any<CancellationToken>());
    }
}
