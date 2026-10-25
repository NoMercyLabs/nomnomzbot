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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the <c>follow</c> Discord trigger: a <see cref="FollowEvent"/> posts the channel's rule to its
/// Discord channel with the placeholders filled, one follow posts once, and no rule means no post.
/// </summary>
public sealed class DiscordFollowNotificationHandlerTests
{
    private static readonly DateTimeOffset FollowedAt = new(2026, 6, 22, 20, 0, 0, TimeSpan.Zero);

    private static FollowEvent Follow(Guid broadcaster, string userId = "42") =>
        new()
        {
            BroadcasterId = broadcaster,
            UserId = userId,
            UserDisplayName = "Viewer One",
            UserLogin = "viewer_one",
            FollowedAt = FollowedAt,
        };

    private static DiscordFollowNotificationHandler Handler(
        NomNomzBot.Application.Contracts.Discord.IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordFollowNotificationHandler>.Instance
        );

    [Fact]
    public async Task Follow_PostsTheRule_ToItsDiscordChannel_WithThePlaceholdersFilled()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("follow", "{user} just followed!");

        await harness.RaiseAsync(Handler, Follow(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
        harness.Gateway.Posts[0].ChannelId.Should().Be(DiscordTriggerHandlerHarness.DiscordChannel);
        harness.Gateway.Posts[0].Message.Content.Should().Be("Viewer One just followed!");
        DiscordNotificationDispatch row = (await harness.DispatchesAsync()).Single();
        row.TriggerType.Should().Be("follow");
        row.Status.Should().Be("sent");
        row.DedupeKey.Should().Be($"follow:twitch:42:{FollowedAt.UtcDateTime:O}");
    }

    [Fact]
    public async Task Follow_DeliveredTwice_PostsOnce()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("follow", "{user} just followed!");

        await harness.RaiseAsync(Handler, Follow(harness.BroadcasterId));
        await harness.RaiseAsync(Handler, Follow(harness.BroadcasterId));

        harness.Gateway.Posts.Should().ContainSingle();
        (await harness.DispatchesAsync())
            .Select(row => row.Status)
            .Should()
            .Equal("sent", "skipped_dupe");
    }

    [Fact]
    public async Task Follow_WithoutAFollowRule_PostsNothing()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("go_live", "live!");

        await harness.RaiseAsync(Handler, Follow(harness.BroadcasterId));

        harness.Gateway.Posts.Should().BeEmpty();
        (await harness.DispatchesAsync()).Should().BeEmpty();
    }

    [Fact]
    public void FollowTrigger_IsInTheServersCatalog_WithItsPlaceholders()
    {
        DiscordNotificationTrigger trigger =
            DiscordConfigDefaults.NotificationTriggerCatalog.Single(entry =>
                entry.Name == "follow"
            );

        trigger.LabelKey.Should().Be("dashboard.discord.triggers.follow");
        trigger.Placeholders.Should().Equal("user", "channel.display");
    }
}
