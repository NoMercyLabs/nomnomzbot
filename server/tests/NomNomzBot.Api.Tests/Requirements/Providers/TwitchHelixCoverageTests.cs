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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Providers;

/// <summary>
/// REQUIREMENT: the typed Twitch Helix client (<see cref="ITwitchHelixClient"/> façade + its ~26 category
/// sub-clients) must expose the FULL documented Helix surface the bot manages — every endpoint category, and
/// within the core categories every documented operation. HARD project rule (external-api-full-management-coverage):
/// anything Helix lets a caller do, the bot exposes; a missing category or operation is a gap to ADD, not to skip.
/// These tests enumerate what the client ACTUALLY exposes (reflection) and compare it to the expected Helix
/// surface built from the Twitch API reference. A red is the concrete backlog of Helix endpoints still to wire.
/// </summary>
public sealed class TwitchHelixCoverageTests
{
    // The full documented Helix endpoint-category set (Twitch API reference), each mapped to the façade
    // accessor(s) that would expose it. Deprecated "Tags" is excluded; "EventSub" rides a separate WebSocket
    // transport (IEventSubTransport), not the REST façade, and is covered by its own suite.
    public static readonly (string Category, string[] Accessors)[] HelixCategories =
    [
        ("Ads", ["Ads"]),
        ("Analytics", ["Analytics"]),
        ("Bits", ["Bits"]),
        ("Channels", ["Channels"]),
        ("Channel Points", ["ChannelPoints"]),
        ("Charity", ["Charity"]),
        ("Chat", ["Chat", "ChatAssets"]),
        ("Clips", ["Clips"]),
        ("Conduits", ["Conduits"]),
        ("Content Classification Labels", ["ContentClassification"]),
        ("Entitlements", ["Entitlements", "Drops"]),
        ("Extensions", ["Extensions"]),
        ("Games", ["Games"]),
        ("Goals", ["Goals"]),
        ("Guest Star", ["GuestStar"]),
        ("Hype Train", ["HypeTrain"]),
        ("Moderation", ["Moderation", "Moderators"]),
        ("Polls", ["Polls"]),
        ("Predictions", ["Predictions"]),
        ("Raids", ["Raids"]),
        ("Schedule", ["Schedule"]),
        ("Search", ["Search"]),
        ("Streams", ["Streams"]),
        ("Subscriptions", ["Subscriptions"]),
        ("Teams", ["Teams"]),
        ("Users", ["Users"]),
        ("Videos", ["Videos"]),
        ("Whispers", ["Whispers"]),
    ];

    [Fact]
    public void Helix_facade_exposes_a_sub_client_for_every_documented_endpoint_category()
    {
        HashSet<string> accessors = typeof(ITwitchHelixClient)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> missing = HelixCategories
            .Where(category => !category.Accessors.Any(accessors.Contains))
            .Select(category => category.Category)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must manage every documented Helix endpoint category; "
                    + $"the façade exposes accessors [{string.Join(", ", accessors.OrderBy(a => a))}] — "
                    + $"missing categories: [{string.Join(", ", missing)}]"
            );
    }

    [Fact]
    public void Moderation_sub_clients_cover_every_documented_moderation_endpoint()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(
            typeof(ITwitchModerationApi),
            typeof(ITwitchModeratorsApi)
        );

        (string Endpoint, string[] Keywords)[] expected =
        [
            ("Ban User", ["BanUser"]),
            ("Unban User", ["UnbanUser"]),
            ("Timeout User", ["TimeoutUser"]),
            ("Get Banned Users", ["GetBannedUsers"]),
            ("Get Unban Requests", ["GetUnbanRequests"]),
            ("Resolve Unban Requests", ["ResolveUnbanRequest"]),
            ("Get Blocked Terms", ["GetBlockedTerms"]),
            ("Add Blocked Term", ["AddBlockedTerm"]),
            ("Remove Blocked Term", ["RemoveBlockedTerm"]),
            ("Delete Chat Messages", ["DeleteChatMessage", "DeleteAllChatMessages"]),
            ("Get Shield Mode Status", ["GetShieldModeStatus"]),
            ("Update Shield Mode Status", ["UpdateShieldModeStatus"]),
            ("Warn Chat User", ["WarnChatUser"]),
            ("Add/Remove Suspicious User", ["SuspiciousStatus"]),
            ("Check AutoMod Status", ["CheckAutoModStatus"]),
            ("Manage Held AutoMod Messages", ["ManageHeldAutoModMessage"]),
            ("Get AutoMod Settings", ["GetAutoModSettings"]),
            ("Update AutoMod Settings", ["UpdateAutoModSettings"]),
            ("Get Moderators", ["GetModerators"]),
            ("Add Moderator", ["AddModerator"]),
            ("Remove Moderator", ["RemoveModerator"]),
            ("Get VIPs", ["GetVips"]),
            ("Add VIP", ["AddVip"]),
            ("Remove VIP", ["RemoveVip"]),
            ("Get Moderated Channels", ["GetModeratedChannels"]),
        ];

        List<string> missing = expected
            .Where(endpoint => !methods.Covers(endpoint.Keywords))
            .Select(endpoint => endpoint.Endpoint)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must expose every Helix moderation endpoint the moderation dashboard manages"
            );
    }

    [Fact]
    public void Chat_sub_clients_cover_every_documented_chat_endpoint()
    {
        // Send/reply is the IChatProvider seam (Helix Send Chat Message); the rest is on the chat sub-clients.
        HashSet<string> methods = ProviderSurface.MethodNames(
            typeof(ITwitchChatApi),
            typeof(ITwitchChatAssetsApi),
            typeof(IChatProvider)
        );

        (string Endpoint, string[] Keywords)[] expected =
        [
            ("Send Chat Message", ["SendMessage"]),
            ("Send Chat Announcement", ["SendAnnouncement"]),
            ("Send a Shoutout", ["SendShoutout"]),
            ("Get Chat Settings", ["GetChatSettings"]),
            ("Update Chat Settings", ["UpdateChatSettings"]),
            ("Get Chatters", ["GetChatters"]),
            ("Get Channel Emotes", ["GetChannelEmotes"]),
            ("Get Global Emotes", ["GetGlobalEmotes"]),
            ("Get Emote Sets", ["GetEmoteSets"]),
            ("Get User Emotes", ["GetUserEmotes"]),
            ("Get Channel Chat Badges", ["GetChannelChatBadges"]),
            ("Get Global Chat Badges", ["GetGlobalChatBadges"]),
            ("Get User Chat Color", ["GetUserChatColor"]),
            ("Update User Chat Color", ["UpdateUserChatColor"]),
            ("Get Shared Chat Session", ["GetSharedChatSession"]),
        ];

        List<string> missing = expected
            .Where(endpoint => !methods.Covers(endpoint.Keywords))
            .Select(endpoint => endpoint.Endpoint)
            .ToList();

        missing.Should().BeEmpty("the bot must expose every Helix chat endpoint");
    }

    [Fact]
    public void Channel_and_broadcast_sub_clients_cover_every_documented_endpoint()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(
            typeof(ITwitchChannelsApi),
            typeof(ITwitchChannelPointsApi),
            typeof(ITwitchStreamsApi),
            typeof(ITwitchVideosApi),
            typeof(ITwitchScheduleApi),
            typeof(ITwitchPollsApi),
            typeof(ITwitchPredictionsApi),
            typeof(ITwitchRaidsApi),
            typeof(ITwitchSubscriptionsApi)
        );

        (string Endpoint, string[] Keywords)[] expected =
        [
            ("Get Channel Information", ["GetChannelInformation"]),
            ("Modify Channel Information", ["ModifyChannelInformation"]),
            ("Get Channel Editors", ["GetChannelEditors"]),
            ("Get Channel Followers", ["GetChannelFollowers"]),
            ("Get Followed Channels", ["GetFollowedChannels"]),
            ("Create Custom Reward", ["CreateCustomReward"]),
            ("Delete Custom Reward", ["DeleteCustomReward"]),
            ("Update Custom Reward", ["UpdateCustomReward"]),
            ("Get Custom Rewards", ["GetCustomRewards"]),
            ("Get Custom Reward Redemptions", ["GetCustomRewardRedemptions"]),
            ("Update Redemption Status", ["UpdateRedemptionStatus"]),
            ("Get Streams", ["GetStreams"]),
            ("Get Followed Streams", ["GetFollowedStreams"]),
            ("Get Stream Key", ["GetStreamKey"]),
            ("Create Stream Marker", ["CreateStreamMarker"]),
            ("Get Stream Markers", ["GetStreamMarkers"]),
            ("Get Videos", ["GetVideos"]),
            ("Delete Videos", ["DeleteVideos"]),
            ("Get Channel Stream Schedule", ["GetSchedule"]),
            ("Update Channel Stream Schedule", ["UpdateScheduleSettings"]),
            ("Create Schedule Segment", ["CreateSegment"]),
            ("Update Schedule Segment", ["UpdateSegment"]),
            ("Delete Schedule Segment", ["DeleteSegment"]),
            ("Get Polls", ["GetPolls"]),
            ("Create Poll", ["CreatePoll"]),
            ("End Poll", ["EndPoll"]),
            ("Get Predictions", ["GetPredictions"]),
            ("Create Prediction", ["CreatePrediction"]),
            ("End Prediction", ["EndPrediction"]),
            ("Start a raid", ["StartRaid"]),
            ("Cancel a raid", ["CancelRaid"]),
            ("Get Broadcaster Subscriptions", ["GetBroadcasterSubscriptions"]),
            ("Check User Subscription", ["CheckUserSubscription"]),
        ];

        List<string> missing = expected
            .Where(endpoint => !methods.Covers(endpoint.Keywords))
            .Select(endpoint => endpoint.Endpoint)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the bot must expose every documented channel/stream/broadcast Helix endpoint"
            );
    }

    [Fact]
    public void User_sub_client_covers_every_documented_user_endpoint()
    {
        HashSet<string> methods = ProviderSurface.MethodNames(typeof(ITwitchUsersApi));

        (string Endpoint, string[] Keywords)[] expected =
        [
            ("Get Users", ["GetUsersByIds", "GetUsersByLogins"]),
            ("Update User", ["UpdateDescription"]),
            ("Get User Block List", ["GetBlockList"]),
            ("Block User", ["BlockUser"]),
            ("Unblock User", ["UnblockUser"]),
            ("Get User Extensions", ["GetInstalledExtensions"]),
            ("Get User Active Extensions", ["GetActiveExtensions"]),
            ("Update User Extensions", ["UpdateActiveExtensions"]),
        ];

        List<string> missing = expected
            .Where(endpoint => !methods.Covers(endpoint.Keywords))
            .Select(endpoint => endpoint.Endpoint)
            .ToList();

        missing.Should().BeEmpty("the bot must expose every documented Helix Users endpoint");
    }
}
