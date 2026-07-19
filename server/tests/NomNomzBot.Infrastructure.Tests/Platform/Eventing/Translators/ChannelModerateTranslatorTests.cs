// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the <c>channel.moderate</c> (v2) mega-event translator. Each proves the general mapping:
/// the <c>action</c> string becomes <see cref="ModerationActionTakenEvent.ActionType"/>, the moderator is always
/// captured, the raw broadcaster id becomes <see cref="ModerationActionTakenEvent.ChannelId"/>, and when the
/// action's same-named nested object names a target chatter that target (and any reason) is lifted onto the
/// event. Covered action variants: a <c>ban</c> (target + reason), a <c>delete</c> (target, no reason), and a
/// settings action (<c>emoteonly</c>, no target).
/// </summary>
public sealed class ChannelModerateTranslatorTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-moderate-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = "channel.moderate",
            SubscriptionVersion = "2",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "423374343",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task ChannelModerate_BanAction_MapsActionModeratorAndTargetWithReason()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModerateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "broadcaster_user_id": "423374343",
                    "moderator_user_id": "424596340",
                    "moderator_user_login": "quotrok",
                    "moderator_user_name": "quotrok",
                    "action": "ban",
                    "followers": null,
                    "ban": {
                        "user_id": "141981764",
                        "user_login": "twitchdev",
                        "user_name": "TwitchDev",
                        "reason": "rule violation"
                    },
                    "timeout": null,
                    "delete": null
                }
                """
            )
        );

        ModerationActionTakenEvent published = bus.EventsOf<ModerationActionTakenEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ChannelId.Should().Be("423374343", "the raw broadcaster id is carried through");
        published.ActionType.Should().Be("ban");
        published.ModeratorId.Should().Be("424596340");
        published
            .TargetUserId.Should()
            .Be("141981764", "the ban action's nested object names the target chatter");
        published.Reason.Should().Be("rule violation");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelModerate_DeleteAction_MapsTargetWithoutReason()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModerateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "broadcaster_user_id": "423374343",
                    "moderator_user_id": "424596340",
                    "action": "delete",
                    "ban": null,
                    "delete": {
                        "user_id": "141981764",
                        "user_login": "twitchdev",
                        "user_name": "TwitchDev",
                        "message_id": "abc-123",
                        "message_body": "bad message"
                    }
                }
                """
            )
        );

        ModerationActionTakenEvent published = bus.EventsOf<ModerationActionTakenEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.ActionType.Should().Be("delete");
        published.ModeratorId.Should().Be("424596340");
        published
            .TargetUserId.Should()
            .Be("141981764", "the delete action's nested object names the target chatter");
        published.Reason.Should().BeNull("the delete object carries no reason field");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelModerate_SettingsAction_HasNoTargetUser()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModerateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "broadcaster_user_id": "423374343",
                    "moderator_user_id": "424596340",
                    "action": "emoteonly",
                    "ban": null,
                    "delete": null,
                    "followers": null
                }
                """
            )
        );

        ModerationActionTakenEvent published = bus.EventsOf<ModerationActionTakenEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.ActionType.Should().Be("emoteonly");
        published.ModeratorId.Should().Be("424596340");
        published
            .TargetUserId.Should()
            .BeEmpty("a settings-only action has no action-named target object");
        published.Reason.Should().BeNull();
    }

    [Fact]
    public async Task ChannelModerate_RaidAction_AlsoPublishesTheOutgoingRaidEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModerateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "broadcaster_user_id": "423374343",
                    "moderator_user_id": "423374343",
                    "action": "raid",
                    "ban": null,
                    "raid": {
                        "user_id": "141981764",
                        "user_login": "twitchdev",
                        "user_name": "TwitchDev",
                        "viewer_count": 42
                    }
                }
                """
            )
        );

        // The generic moderation feed still fires…
        bus.EventsOf<ModerationActionTakenEvent>()
            .Should()
            .ContainSingle()
            .Which.ActionType.Should()
            .Be("raid");
        // …AND the outgoing-raid split carries the target + viewer count (channel.raid is incoming-only,
        // so this is the ONE truthful source for channel.raid.out).
        OutgoingRaidEvent outgoing = bus.EventsOf<OutgoingRaidEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        outgoing.BroadcasterId.Should().Be(tenant);
        outgoing.ToUserId.Should().Be("141981764");
        outgoing.ToLogin.Should().Be("twitchdev");
        outgoing.ToDisplayName.Should().Be("TwitchDev");
        outgoing.ViewerCount.Should().Be(42);
    }

    [Fact]
    public async Task ChannelModerate_UnraidAction_DoesNotPublishAnOutgoingRaid()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModerateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                """
                {
                    "broadcaster_user_id": "423374343",
                    "moderator_user_id": "423374343",
                    "action": "unraid",
                    "unraid": {
                        "user_id": "141981764",
                        "user_login": "twitchdev",
                        "user_name": "TwitchDev"
                    }
                }
                """
            )
        );

        bus.EventsOf<ModerationActionTakenEvent>().Should().ContainSingle();
        bus.EventsOf<OutgoingRaidEvent>().Should().BeEmpty("cancelling a raid is not raiding out");
    }
}
