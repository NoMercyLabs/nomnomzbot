// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the slice-3 chat seam: the router (registered as THE <see cref="IChatProvider"/>) selects the
/// platform by the tenant channel's <c>Channel.Provider</c> — a YouTube tenant's send reaches the YouTube
/// platform, a Twitch tenant's the Twitch one, and an unknown/unregistered provider falls back to Twitch
/// (the pre-seam behavior) instead of throwing into the hot chat path. It also proves S009b: the router
/// stamps every outbound line with <see cref="BotEmittedLine.Marker"/> BEFORE handing it to whichever
/// platform is selected — the one seam a future platform inherits the loop-guard from automatically.
/// </summary>
public sealed class ChatPlatformRouterTests
{
    private static readonly Guid TwitchTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid YouTubeTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid KickTenant = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000c9");

    private static async Task<(
        ChatPlatformRouter Router,
        IChatPlatform Twitch,
        IChatPlatform YouTube
    )> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(TwitchTenant, AuthEnums.Platform.Twitch, "tw1"));
        db.Channels.Add(Channel(YouTubeTenant, AuthEnums.Platform.YouTube, "UCyt"));
        db.Channels.Add(Channel(KickTenant, AuthEnums.Platform.Kick, "kick1"));
        await db.SaveChangesAsync();

        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        IChatPlatform youtube = Substitute.For<IChatPlatform>();
        youtube.Provider.Returns(AuthEnums.Platform.YouTube);

        ChatPlatformRouter router = new(
            [twitch, youtube],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );
        return (router, twitch, youtube);
    }

    [Fact]
    public async Task A_youtube_tenants_send_routes_to_the_youtube_platform()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform youtube) =
            await BuildAsync();

        await router.SendMessageAsync(YouTubeTenant, "hello");

        await youtube
            .Received(1)
            .SendMessageAsync(
                YouTubeTenant,
                BotEmittedLine.Marker + "hello",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_twitch_tenants_send_routes_to_the_twitch_platform()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform youtube) =
            await BuildAsync();

        await router.SendMessageAsync(TwitchTenant, "hi");
        await router.SendReplyAsync(TwitchTenant, "m-1", "reply");

        await twitch
            .Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "hi",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .Received(1)
            .SendReplyAsync(
                TwitchTenant,
                "m-1",
                BotEmittedLine.Marker + "reply",
                Arg.Any<CancellationToken>()
            );
        await youtube
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unregistered_provider_falls_back_to_twitch_instead_of_throwing()
    {
        // Kick has a Channel row but no registered platform yet — the router must not blow up chat.
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync();

        await router.SendMessageAsync(KickTenant, "yo");

        await twitch
            .Received(1)
            .SendMessageAsync(
                KickTenant,
                BotEmittedLine.Marker + "yo",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Every_send_and_reply_is_stamped_with_the_bot_emitted_marker_regardless_of_platform()
    {
        // S009b: the stamp happens at the ROUTER, not inside each platform — so a future platform
        // registered as IChatPlatform inherits the loop-guard for free, with zero code of its own.
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform youtube) =
            await BuildAsync();

        await router.SendMessageAsync(TwitchTenant, "twitch line");
        await router.SendReplyAsync(YouTubeTenant, "parent-1", "youtube reply");

        await twitch
            .Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "twitch line",
                Arg.Any<CancellationToken>()
            );
        await youtube
            .Received(1)
            .SendReplyAsync(
                YouTubeTenant,
                "parent-1",
                BotEmittedLine.Marker + "youtube reply",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Moderation_operations_route_by_the_same_provider_key()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform youtube) =
            await BuildAsync();

        await router.TimeoutUserAsync(TwitchTenant, "u1", 60, "spam");
        await router.DeleteMessageAsync(YouTubeTenant, "m-9");

        await twitch
            .Received(1)
            .TimeoutUserAsync(TwitchTenant, "u1", 60, "spam", Arg.Any<CancellationToken>());
        await youtube
            .Received(1)
            .DeleteMessageAsync(YouTubeTenant, "m-9", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_long_line_reaches_the_platform_as_ordered_chunks_within_its_limit_and_the_marker_survives_the_loop_guard()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync();
        List<string> captured = [];
        twitch
            .SendMessageAsync(TwitchTenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true)
            .AndDoes(call => captured.Add(call.ArgAt<string>(1)));

        string longLine = string.Join(" ", Enumerable.Range(1, 130).Select(i => $"word{i}"));
        bool sent = await router.SendMessageAsync(TwitchTenant, longLine);

        Assert.True(sent);
        Assert.True(captured.Count > 1);
        Assert.All(captured, c => Assert.True(c.Replace(BotEmittedLine.Marker, "").Length <= 500));
        // The marker rides only the first chunk...
        Assert.True(BotEmittedLine.IsMarked(captured[0]));
        Assert.All(captured.Skip(1), c => Assert.False(BotEmittedLine.IsMarked(c)));
        // ...and still trips the loop guard's own detection mechanism if that chunk is fed back through
        // ingest — the exact signal IBotSelfEchoGuard falls back to for the self-host (same-account) case.
        Assert.True(BotEmittedLine.IsMarked(captured[0]));
    }

    [Fact]
    public async Task Two_identical_consecutive_sends_both_reach_the_platform_the_second_varied()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync();
        List<string> captured = [];
        twitch
            .SendMessageAsync(TwitchTenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true)
            .AndDoes(call => captured.Add(call.ArgAt<string>(1)));

        bool firstSent = await router.SendMessageAsync(TwitchTenant, "gg well played");
        bool secondSent = await router.SendMessageAsync(TwitchTenant, "gg well played");

        Assert.True(firstSent);
        Assert.True(secondSent);
        Assert.Equal(2, captured.Count);
        Assert.NotEqual(captured[0], captured[1]);
        // The variation is invisible to a human reader once both the loop-guard and variation markers
        // are stripped back out.
        string secondVisible = captured[1]
            .Replace(BotEmittedLine.Marker, "")
            .Replace(OutboundChatShaper.VariationMarker, "");
        Assert.Equal("gg well played", secondVisible);
    }

    [Fact]
    public async Task A_rejected_chunk_is_reported_as_a_failed_send_not_swallowed()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync();
        twitch
            .SendMessageAsync(TwitchTenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        bool sent = await router.SendMessageAsync(TwitchTenant, "hello");

        Assert.False(sent);
    }

    private static Channel Channel(Guid id, string provider, string externalId) =>
        new()
        {
            Id = id,
            OwnerUserId = Owner,
            Provider = provider,
            ExternalChannelId = externalId,
            TwitchChannelId = provider == AuthEnums.Platform.Twitch ? externalId : null,
            Name = externalId,
            NameNormalized = externalId.ToLowerInvariant(),
            IsOnboarded = true,
            DeploymentMode = AuthEnums.DeploymentMode.Saas,
            BillingTierKey = "free",
        };
}
