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
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the shared moderation seam: automatic safety acts on the platform a message actually arrived
/// on (explicit provider key), never on the channel's primary platform and never by falling through to
/// Twitch. Every action reports the platform's real result as an honest outcome.
/// </summary>
public sealed class InboundOriginModeratorTests
{
    // A Twitch-primary channel: a Kick message must still be moderated on Kick.
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000d9");

    private static async Task<(
        IInboundOriginModerator Moderator,
        IChatPlatform Twitch,
        IChatPlatform Kick
    )> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw1",
                TwitchChannelId = "tw1",
                Name = "tw1",
                NameNormalized = "tw1",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        await db.SaveChangesAsync();

        IChatPlatform twitch = Substitute.For<IChatPlatform>();
        twitch.Provider.Returns(AuthEnums.Platform.Twitch);
        IChatPlatform kick = Substitute.For<IChatPlatform>();
        kick.Provider.Returns(AuthEnums.Platform.Kick);

        ChatPlatformRouter router = new(
            [twitch, kick],
            db,
            new OutboundChatShaper(),
            new TokenBucketChatSendQueue(),
            NullLogger<ChatPlatformRouter>.Instance
        );
        return (router, twitch, kick);
    }

    private static IEnumerable<string> ModerationCalls(IChatPlatform platform) =>
        platform
            .ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Where(name => name != "get_Provider");

    [Fact]
    public async Task A_kick_delete_reaches_the_kick_platform_only_and_reports_done()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();
        kick.DeleteMessageAsync(Tenant, "m-1", Arg.Any<CancellationToken>()).Returns(true);

        InboundModerationOutcome outcome = await moderator.DeleteMessageAsync(
            Tenant,
            AuthEnums.Platform.Kick,
            "m-1"
        );

        Assert.Equal(InboundModerationStatus.Done, outcome.Status);
        Assert.Null(outcome.Reason);
        await kick.Received(1).DeleteMessageAsync(Tenant, "m-1", Arg.Any<CancellationToken>());
        Assert.Empty(ModerationCalls(twitch));
    }

    [Fact]
    public async Task A_delete_the_platform_refuses_reports_failed_with_a_reason()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();
        kick.DeleteMessageAsync(Tenant, "m-2", Arg.Any<CancellationToken>()).Returns(false);

        InboundModerationOutcome outcome = await moderator.DeleteMessageAsync(
            Tenant,
            AuthEnums.Platform.Kick,
            "m-2"
        );

        Assert.Equal(InboundModerationStatus.Failed, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
        Assert.Contains(AuthEnums.Platform.Kick, outcome.Reason);
        await kick.Received(1).DeleteMessageAsync(Tenant, "m-2", Arg.Any<CancellationToken>());
        Assert.Empty(ModerationCalls(twitch));
    }

    [Fact]
    public async Task A_delete_on_an_unknown_provider_is_not_supported_and_reaches_no_platform()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();

        InboundModerationOutcome outcome = await moderator.DeleteMessageAsync(
            Tenant,
            AuthEnums.Platform.YouTube,
            "m-3"
        );

        Assert.Equal(InboundModerationStatus.NotSupported, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
        Assert.Empty(ModerationCalls(twitch));
        Assert.Empty(ModerationCalls(kick));
    }

    [Fact]
    public async Task A_kick_timeout_reaches_kick_with_its_arguments_and_reports_the_result()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();
        kick.TimeoutUserAsync(Tenant, "u-1", 600, "spam", Arg.Any<CancellationToken>())
            .Returns(true);

        InboundModerationOutcome done = await moderator.TimeoutUserAsync(
            Tenant,
            AuthEnums.Platform.Kick,
            "u-1",
            600,
            "spam"
        );

        Assert.Equal(InboundModerationStatus.Done, done.Status);
        await kick.Received(1)
            .TimeoutUserAsync(Tenant, "u-1", 600, "spam", Arg.Any<CancellationToken>());
        Assert.Empty(ModerationCalls(twitch));

        kick.TimeoutUserAsync(Tenant, "u-2", 60, null, Arg.Any<CancellationToken>())
            .Returns(false);
        InboundModerationOutcome failed = await moderator.TimeoutUserAsync(
            Tenant,
            AuthEnums.Platform.Kick,
            "u-2",
            60
        );
        Assert.Equal(InboundModerationStatus.Failed, failed.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
    }

    [Fact]
    public async Task A_timeout_on_an_unknown_provider_is_not_supported_and_reaches_no_platform()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();

        InboundModerationOutcome outcome = await moderator.TimeoutUserAsync(
            Tenant,
            "x-live",
            "u-1",
            60
        );

        Assert.Equal(InboundModerationStatus.NotSupported, outcome.Status);
        Assert.Empty(ModerationCalls(twitch));
        Assert.Empty(ModerationCalls(kick));
    }

    [Fact]
    public async Task A_twitch_ban_reaches_twitch_only_and_a_kick_ban_reaches_kick_only()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();
        twitch.BanUserAsync(Tenant, "u-9", "raid", Arg.Any<CancellationToken>()).Returns(true);
        kick.BanUserAsync(Tenant, "u-7", null, Arg.Any<CancellationToken>()).Returns(false);

        InboundModerationOutcome twitchBan = await moderator.BanUserAsync(
            Tenant,
            AuthEnums.Platform.Twitch,
            "u-9",
            "raid"
        );
        InboundModerationOutcome kickBan = await moderator.BanUserAsync(
            Tenant,
            AuthEnums.Platform.Kick,
            "u-7"
        );

        Assert.Equal(InboundModerationStatus.Done, twitchBan.Status);
        Assert.Equal(InboundModerationStatus.Failed, kickBan.Status);
        await twitch.Received(1).BanUserAsync(Tenant, "u-9", "raid", Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceive()
            .BanUserAsync(Tenant, "u-7", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await kick.Received(1).BanUserAsync(Tenant, "u-7", null, Arg.Any<CancellationToken>());
        await kick
            .DidNotReceive()
            .BanUserAsync(Tenant, "u-9", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ban_on_an_unknown_provider_is_not_supported_and_reaches_no_platform()
    {
        (IInboundOriginModerator moderator, IChatPlatform twitch, IChatPlatform kick) =
            await BuildAsync();

        InboundModerationOutcome outcome = await moderator.BanUserAsync(
            Tenant,
            AuthEnums.Platform.YouTube,
            "u-1"
        );

        Assert.Equal(InboundModerationStatus.NotSupported, outcome.Status);
        Assert.Empty(ModerationCalls(twitch));
        Assert.Empty(ModerationCalls(kick));
    }
}
