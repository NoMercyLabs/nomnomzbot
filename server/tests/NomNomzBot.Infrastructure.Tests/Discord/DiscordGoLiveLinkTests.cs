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
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Discord.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the go-live post carries the link placeholders: the channel link, the streamer's picture and the
/// live preview picture. The real dispatcher, template engine and database run; only the Discord gateway is
/// recorded, so the assertions read what would have reached Discord.
/// </summary>
public sealed class DiscordGoLiveLinkTests
{
    private static readonly DateTimeOffset WentLive = new(2026, 6, 22, 20, 0, 0, TimeSpan.Zero);

    private static DiscordGoLiveNotificationHandler Handler(
        IDiscordNotificationDispatcher dispatcher
    ) =>
        new(
            dispatcher,
            new OutboundSanctionAccessor(),
            NullLogger<DiscordGoLiveNotificationHandler>.Instance
        );

    private static ChannelOnlineEvent LiveEvent(Guid broadcaster, string provider) =>
        new()
        {
            Provider = provider,
            BroadcasterId = broadcaster,
            BroadcasterDisplayName = "Stoney_Eagle",
            StreamTitle = "blame the lag",
            GameName = "Just Chatting",
            StartedAt = WentLive,
        };

    [Fact]
    public async Task GoLive_OnTwitch_PostsTheChannelLink_InTheMessage()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                "{channel.name} is live: {game}. Come hang out: {channel.url}"
            );
        await harness.ShapeChannelAsync(AuthEnums.Platform.Twitch, "Stoney_Eagle", null);

        await harness.RaiseAsync(
            Handler,
            LiveEvent(harness.BroadcasterId, AuthEnums.Platform.Twitch)
        );

        harness.Gateway.Posts.Should().ContainSingle();
        harness
            .Gateway.Posts[0]
            .Message.Content.Should()
            .Be(
                "Stoney_Eagle is live: Just Chatting. Come hang out: https://www.twitch.tv/stoney_eagle"
            );
    }

    [Fact]
    public async Task GoLive_OnKick_PostsTheKickLink()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                "{stream.url} {channel.login}"
            );
        await harness.ShapeChannelAsync(AuthEnums.Platform.Kick, "Stoney_Eagle", null);

        await harness.RaiseAsync(
            Handler,
            LiveEvent(harness.BroadcasterId, AuthEnums.Platform.Kick)
        );

        harness
            .Gateway.Posts[0]
            .Message.Content.Should()
            .Be("https://kick.com/stoney_eagle stoney_eagle");
    }

    [Fact]
    public async Task GoLive_OnTwitch_FillsThePictureFields_FromTheAvatarAndTheStampedPreview()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                "live",
                new(null, null, null, "{channel.avatar}", "{stream.thumbnail}", null, null)
            );
        await harness.ShapeChannelAsync(
            AuthEnums.Platform.Twitch,
            "Stoney_Eagle",
            "https://cdn.example/avatar.png"
        );

        await harness.RaiseAsync(
            Handler,
            LiveEvent(harness.BroadcasterId, AuthEnums.Platform.Twitch)
        );

        DiscordEmbedDto embed = harness.Gateway.Posts[0].Message.Embed!;
        embed.ThumbnailUrl.Should().Be("https://cdn.example/avatar.png");
        embed
            .ImageUrl.Should()
            .Be(
                $"https://static-cdn.jtvnw.net/previews-ttv/live_user_stoney_eagle-1280x720.jpg?t={WentLive.ToUnixTimeSeconds()}"
            );
    }

    [Fact]
    public async Task GoLive_OnKick_LeavesTheEmptyPictureFieldsOutOfThePost()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync(
                "go_live",
                "live",
                new("{title}", null, null, null, "{stream.thumbnail}", null, null)
            );
        await harness.ShapeChannelAsync(AuthEnums.Platform.Kick, "Stoney_Eagle", null);

        await harness.RaiseAsync(
            Handler,
            LiveEvent(harness.BroadcasterId, AuthEnums.Platform.Kick)
        );

        DiscordEmbedDto embed = harness.Gateway.Posts[0].Message.Embed!;
        embed.Title.Should().Be("blame the lag");
        embed
            .ImageUrl.Should()
            .BeNull("Kick has no preview picture, and an empty address is refused");
    }

    [Fact]
    public async Task Follow_AlsoGetsTheChannelLink()
    {
        using DiscordTriggerHandlerHarness harness =
            await DiscordTriggerHandlerHarness.WithRuleAsync("follow", "followed! {channel.url}");
        await harness.ShapeChannelAsync(AuthEnums.Platform.Twitch, "Stoney_Eagle", null);

        await harness.RaiseAsync(
            dispatcher => new DiscordFollowNotificationHandler(
                dispatcher,
                new OutboundSanctionAccessor(),
                NullLogger<DiscordFollowNotificationHandler>.Instance
            ),
            new NomNomzBot.Domain.Community.Events.FollowEvent
            {
                BroadcasterId = harness.BroadcasterId,
                UserId = "42",
                UserDisplayName = "Viewer One",
                UserLogin = "viewer_one",
                FollowedAt = WentLive,
            }
        );

        harness
            .Gateway.Posts[0]
            .Message.Content.Should()
            .Be("followed! https://www.twitch.tv/stoney_eagle");
    }
}
