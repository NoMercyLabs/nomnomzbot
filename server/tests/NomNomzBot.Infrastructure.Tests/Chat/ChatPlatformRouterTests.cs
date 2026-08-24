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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
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
/// platform, a Twitch tenant's the Twitch one. S021: an unknown/unregistered provider is dropped with a
/// warning — it is NEVER silently routed to Twitch or any other platform — while still never throwing into
/// the hot chat path. It also proves S009b: the router stamps every outbound line with
/// <see cref="BotEmittedLine.Marker"/> BEFORE handing it to whichever platform is selected — the one seam a
/// future platform inherits the loop-guard from automatically.
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

    private static async Task<(
        ChatPlatformRouter Router,
        IChatPlatform Twitch,
        IChatPlatform Kick
    )> BuildWithTwitchAndKickAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(Channel(TwitchTenant, AuthEnums.Platform.Twitch, "tw1"));
        await db.SaveChangesAsync();

        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        twitch
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        twitch
            .SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        IChatPlatform kick = Substitute.For<IChatPlatform>();
        kick.Provider.Returns(AuthEnums.Platform.Kick);
        kick.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        kick.SendReplyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(true);

        ChatPlatformRouter router = new(
            [twitch, kick],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );
        return (router, twitch, kick);
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
    public async Task An_unregistered_provider_is_dropped_honestly_never_routed_to_twitch()
    {
        // Kick has a Channel row but no registered platform yet — the router must not blow up chat,
        // and (S021) must NEVER silently reroute the send to Twitch just because Twitch is registered.
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync();

        bool sent = await router.SendMessageAsync(KickTenant, "yo");

        Assert.False(sent);
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
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

    // ---- S021: IInboundOriginChatSender — route by the platform the INBOUND message arrived on ----

    [Fact]
    public async Task A_kick_origin_send_reaches_kick_and_never_twitch_even_though_the_tenant_is_a_twitch_channel()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform kick) =
            await BuildWithTwitchAndKickAsync();
        IInboundOriginChatSender sender = router;

        Result result = await sender.SendMessageAsync(
            TwitchTenant,
            AuthEnums.Platform.Kick,
            "up 2h30m"
        );

        Assert.True(result.IsSuccess);
        await kick.Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "up 2h30m",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_twitch_origin_send_reaches_twitch_and_never_kick()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform kick) =
            await BuildWithTwitchAndKickAsync();
        IInboundOriginChatSender sender = router;

        Result result = await sender.SendMessageAsync(
            TwitchTenant,
            AuthEnums.Platform.Twitch,
            "up 2h30m"
        );

        Assert.True(result.IsSuccess);
        await twitch
            .Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "up 2h30m",
                Arg.Any<CancellationToken>()
            );
        await kick.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unsupported_provider_is_an_honest_result_failure_with_no_send_on_any_platform()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform kick) =
            await BuildWithTwitchAndKickAsync();
        IInboundOriginChatSender sender = router;

        Result result = await sender.SendMessageAsync(TwitchTenant, "discord", "hi");

        Assert.True(result.IsFailure);
        Assert.Equal("unsupported_provider", result.ErrorCode);
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
        await kick.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Successive_messages_from_different_providers_on_the_same_channel_each_route_to_their_own_provider_never_cached_from_the_first()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform kick) =
            await BuildWithTwitchAndKickAsync();
        IInboundOriginChatSender sender = router;

        await sender.SendMessageAsync(TwitchTenant, AuthEnums.Platform.Kick, "kick first");
        await sender.SendMessageAsync(TwitchTenant, AuthEnums.Platform.Twitch, "twitch second");

        await kick.Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "kick first",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .Received(1)
            .SendMessageAsync(
                TwitchTenant,
                BotEmittedLine.Marker + "twitch second",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_inbound_origin_reply_routes_to_its_own_provider_and_an_unsupported_provider_reply_fails_honestly()
    {
        (ChatPlatformRouter router, IChatPlatform twitch, IChatPlatform kick) =
            await BuildWithTwitchAndKickAsync();
        IInboundOriginChatSender sender = router;

        Result kickReply = await sender.SendReplyAsync(
            TwitchTenant,
            AuthEnums.Platform.Kick,
            "kick-msg-1",
            "reply"
        );
        Result unsupportedReply = await sender.SendReplyAsync(
            TwitchTenant,
            "discord",
            "d-msg-1",
            "reply"
        );

        Assert.True(kickReply.IsSuccess);
        Assert.True(unsupportedReply.IsFailure);
        Assert.Equal("unsupported_provider", unsupportedReply.ErrorCode);
        await kick.Received(1)
            .SendReplyAsync(
                TwitchTenant,
                "kick-msg-1",
                BotEmittedLine.Marker + "reply",
                Arg.Any<CancellationToken>()
            );
        await twitch
            .DidNotReceiveWithAnyArgs()
            .SendReplyAsync(default, default!, default!, Arg.Any<CancellationToken>());
    }

    private static Channel Channel(
        Guid id,
        string provider,
        string externalId,
        string? botLinePrefix = null
    ) =>
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
            BotLinePrefix = botLinePrefix,
        };
}

/// <summary>
/// S011 (D5 bot-line prefix): proves the visible <c>Channel.BotLinePrefix</c> lands on outbound bot lines
/// exactly once — never per chunk — counts toward the platform's character budget, and is skipped entirely
/// once a dedicated bot account is connected for the tenant. Separate fixture from
/// <see cref="ChatPlatformRouterTests"/> so each test builds its own tenant with the exact bot-authorization
/// state it needs.
/// </summary>
public sealed class ChatPlatformRouterBotLinePrefixTests
{
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000d9");

    private static async Task<(
        ChatPlatformRouter Router,
        IChatPlatform Twitch,
        AuthDbContext Db
    )> BuildAsync(Guid tenantId, string? botLinePrefix, bool withDedicatedBot = false)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = tenantId,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-prefix",
                TwitchChannelId = "tw-prefix",
                Name = "tw-prefix",
                NameNormalized = "tw-prefix",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
                BotLinePrefix = botLinePrefix,
            }
        );

        if (withDedicatedBot)
        {
            BotAccount bot = new()
            {
                Id = Guid.NewGuid(),
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = "dedicated-bot-1",
                BotUsername = "NomNomzBot",
                IdentityType = AuthEnums.BotIdentityType.Shared,
                IsActive = true,
                ConnectionId = Guid.NewGuid(),
            };
            db.BotAccounts.Add(bot);
        }

        await db.SaveChangesAsync();

        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        twitch
            .SendMessageAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        ChatPlatformRouter router = new(
            [twitch],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );
        return (router, twitch, db);
    }

    [Fact]
    public async Task A_configured_prefix_lands_on_the_bot_line_exactly_once()
    {
        Guid tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync(tenant, "*");

        await router.SendMessageAsync(tenant, "hello chat");

        await twitch
            .Received(1)
            .SendMessageAsync(
                tenant,
                BotEmittedLine.Marker + "*hello chat",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task No_configured_prefix_leaves_the_bot_line_unprefixed()
    {
        Guid tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e2");
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync(tenant, null);

        await router.SendMessageAsync(tenant, "hello chat");

        await twitch
            .Received(1)
            .SendMessageAsync(
                tenant,
                BotEmittedLine.Marker + "hello chat",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_line_that_chunks_into_three_carries_the_prefix_on_only_the_first_chunk()
    {
        Guid tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e3");
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync(tenant, "*");
        List<string> captured = [];
        twitch
            .SendMessageAsync(tenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true)
            .AndDoes(call => captured.Add(call.ArgAt<string>(1)));

        // 260 words of ~7 chars each (~1800 chars) comfortably exceeds Twitch's 500-char budget three times over.
        string longLine = string.Join(" ", Enumerable.Range(1, 260).Select(i => $"word{i}"));
        await router.SendMessageAsync(tenant, longLine);

        Assert.True(captured.Count >= 3);
        // The prefix rides the first chunk, immediately after the (invisible) loop-guard marker...
        Assert.Equal(
            BotEmittedLine.Marker + "*",
            captured[0][..(BotEmittedLine.Marker.Length + 1)]
        );
        // ...and appears nowhere else, so a chunked line never shows three stray prefixes mid-sentence.
        Assert.All(captured.Skip(1), c => Assert.DoesNotContain("*", c));
        // The prefix counts toward the platform's visible-character budget: no chunk (marker stripped)
        // exceeds the limit even though the first chunk carries an extra visible character.
        Assert.All(captured, c => Assert.True(c.Replace(BotEmittedLine.Marker, "").Length <= 500));
    }

    [Fact]
    public async Task A_dedicated_bot_account_suppresses_the_prefix_entirely()
    {
        Guid tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e4");
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync(
            tenant,
            "*",
            withDedicatedBot: true
        );

        await router.SendMessageAsync(tenant, "hello chat");

        // Its own connected username already tells viewers apart from the streamer, so the courtesy
        // prefix is redundant and is skipped — only the always-on loop-guard marker rides the line.
        await twitch
            .Received(1)
            .SendMessageAsync(
                tenant,
                BotEmittedLine.Marker + "hello chat",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_prefixed_chunk_still_trips_the_loop_guards_marker_check_if_fed_back_through_ingest()
    {
        Guid tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000e5");
        (ChatPlatformRouter router, IChatPlatform twitch, _) = await BuildAsync(tenant, "#");
        string? sentLine = null;
        twitch
            .SendMessageAsync(tenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true)
            .AndDoes(call => sentLine = call.ArgAt<string>(1));

        await router.SendMessageAsync(tenant, "hi");

        Assert.NotNull(sentLine);
        Assert.True(BotEmittedLine.IsMarked(sentLine!));
    }
}
