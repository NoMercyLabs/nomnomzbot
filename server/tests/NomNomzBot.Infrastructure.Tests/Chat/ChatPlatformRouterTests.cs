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
