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
/// and raw lowercase Twitch wire topics are kept off the wire. Default-public: anything not matched is forwarded.
/// </summary>
public sealed class OverlayEventFilterTests
{
    [Theory]
    [InlineData("ChatMessageReceivedEvent")]
    [InlineData("FollowEvent")]
    [InlineData("NewSubscriptionEvent")]
    [InlineData("CheerEvent")]
    [InlineData("RaidEvent")]
    [InlineData("PlaybackStateChangedEvent")]
    [InlineData("HypeTrainBeganEvent")]
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
}
