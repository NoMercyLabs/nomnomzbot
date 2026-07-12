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
using NomNomzBot.Infrastructure.Overlays;

namespace NomNomzBot.Infrastructure.Tests.Overlays;

/// <summary>
/// Proves the overlay-feed eligibility rule (widgets-overlays.md): user-facing domain events + custom/supporter
/// feeds reach overlays; internal plumbing families (EventSub/Helix/Integration/Deployment/Projection/Authorization)
/// and raw lowercase Twitch wire topics are kept off the wire; and events re-broadcast in a decorated form by a
/// dedicated handler (chat + every user-facing alert) are dropped so the raw journaled duplicate never rides the
/// feed. Default-public: anything not matched is forwarded.
/// </summary>
public sealed class OverlayEventFilterTests
{
    [Theory]
    // Still forwarded verbatim: a persistent now-playing state, TTS dispatch, and the custom/supporter feeds have
    // no decorated re-broadcast, so the raw journaled event IS the overlay event.
    [InlineData("PlaybackStateChangedEvent")]
    [InlineData("TtsUtteranceDispatchedEvent")]
    [InlineData("custom.heartrate")]
    [InlineData("supporter.tip")]
    public void ShouldForward_UserFacingEvents_AreForwarded(string eventType)
    {
        OverlayEventFilter.ShouldForward(eventType).Should().BeTrue();
    }

    [Theory]
    [InlineData("EventSubSubscriptionStatusChangedEvent")]
    [InlineData("EventSubConnectedEvent")]
    [InlineData("TwitchHelixReauthRequiredEvent")]
    [InlineData("TwitchHelixRateLimitedEvent")]
    [InlineData("IntegrationTokenRefreshedEvent")]
    [InlineData("DeploymentProfileResolvedEvent")]
    [InlineData("AuthorizationDeniedEvent")]
    [InlineData("channel.chat.message")] // raw wire duplicate of ChatMessageReceivedEvent
    [InlineData("stream.online")]
    [InlineData("")]
    public void ShouldForward_InternalOrRawTopics_AreDropped(string eventType)
    {
        OverlayEventFilter.ShouldForward(eventType).Should().BeFalse();
    }

    [Theory]
    // Each of these reaches overlays instead as a DECORATED overlay event (chat via ChatMessageBroadcastHandler; the
    // alerts via their dashboard broadcast handler + OverlayAlertBroadcast), carrying render-ready fields the raw
    // journaled event lacks (emotes/badges/avatar/pronouns/community standing + resolved amounts). The raw form must
    // NOT also ride the generic feed — a widget would otherwise get a useless duplicate it cannot build from.
    [InlineData("ChatMessageReceivedEvent")]
    [InlineData("FollowEvent")]
    [InlineData("NewSubscriptionEvent")]
    [InlineData("ResubscriptionEvent")]
    [InlineData("GiftSubscriptionEvent")]
    [InlineData("CheerEvent")]
    [InlineData("RaidEvent")]
    [InlineData("RewardRedeemedEvent")]
    [InlineData("ModeratorAddedEvent")]
    [InlineData("ModeratorRemovedEvent")]
    [InlineData("VipAddedEvent")]
    [InlineData("VipRemovedEvent")]
    [InlineData("ShoutoutReceivedEvent")]
    [InlineData("UserBannedEvent")]
    [InlineData("UserTimedOutEvent")]
    [InlineData("UserUnbannedEvent")]
    [InlineData("HypeTrainBeganEvent")]
    [InlineData("HypeTrainProgressEvent")]
    [InlineData("HypeTrainEndedEvent")]
    public void ShouldForward_EventsDecoratedElsewhere_AreDropped(string eventType)
    {
        OverlayEventFilter.ShouldForward(eventType).Should().BeFalse();
    }
}
