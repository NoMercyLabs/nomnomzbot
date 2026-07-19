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
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing.Translators;

/// <summary>
/// Behaviour tests for the moderation fan-out translators: each proves a real EventSub payload is parsed into
/// the matching typed domain event with the resolved tenant, injected clock, and correctly mapped fields.
/// channel.ban is covered in both branches — a permanent ban yields <see cref="UserBannedEvent"/>, a timeout
/// (with <c>ends_at</c>) yields <see cref="UserTimedOutEvent"/> with the derived duration.
/// </summary>
public sealed class ModerationTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(
        Guid tenant,
        string type,
        string payload,
        string twitchBroadcasterId = "broadcaster-99"
    )
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "1",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = twitchBroadcasterId,
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task ChannelBan_PermanentBan_PublishesUserBannedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelBanTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.ban",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "broadcaster_user_id": "broadcaster-99",
                    "moderator_user_id": "mod-1",
                    "moderator_user_name": "Mod_One",
                    "reason": "spamming",
                    "banned_at": "2026-06-20T11:29:00Z",
                    "ends_at": null,
                    "is_permanent": true
                }
                """
            )
        );

        bus.EventsOf<UserTimedOutEvent>().Should().BeEmpty("a permanent ban is not a timeout");
        UserBannedEvent published = bus.EventsOf<UserBannedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.TargetUserId.Should().Be("1234");
        published.TargetDisplayName.Should().Be("Cool_User");
        published.ModeratorUserId.Should().Be("mod-1");
        published
            .ModeratorDisplayName.Should()
            .Be("Mod_One", "the channel.ban notice's {moderator} variable needs the display name");
        published.Reason.Should().Be("spamming");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelBan_Timeout_PublishesUserTimedOutEvent_WithDerivedDuration()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelBanTranslator translator = new(bus, Clock);

        // banned_at .. ends_at spans 600 seconds (10 minutes).
        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.ban",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "moderator_user_id": "mod-1",
                    "reason": "cool down",
                    "banned_at": "2026-06-20T11:00:00Z",
                    "ends_at": "2026-06-20T11:10:00Z",
                    "is_permanent": false
                }
                """
            )
        );

        bus.EventsOf<UserBannedEvent>().Should().BeEmpty("a timeout is not a permanent ban");
        UserTimedOutEvent published = bus.EventsOf<UserTimedOutEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.TargetUserId.Should().Be("1234");
        published.TargetDisplayName.Should().Be("Cool_User");
        published.ModeratorUserId.Should().Be("mod-1");
        published.Reason.Should().Be("cool down");
        published
            .DurationSeconds.Should()
            .Be(600, "the duration is derived from ends_at - banned_at");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelUnban_PublishesUserUnbannedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelUnbanTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.unban",
                """
                {
                    "user_id": "1234",
                    "user_login": "cool_user",
                    "user_name": "Cool_User",
                    "moderator_user_id": "mod-1",
                    "moderator_user_name": "Mod_One"
                }
                """
            )
        );

        UserUnbannedEvent published = bus.EventsOf<UserUnbannedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.TargetUserId.Should().Be("1234");
        published.TargetDisplayName.Should().Be("Cool_User");
        published.ModeratorUserId.Should().Be("mod-1");
        published.ModeratorDisplayName.Should().Be("Mod_One");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelUnbanRequestCreate_PublishesUnbanRequestCreatedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelUnbanRequestCreateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.unban_request.create",
                """
                {
                    "id": "60",
                    "user_id": "1339",
                    "user_login": "not_cool_user",
                    "user_name": "Not_Cool_User",
                    "text": "unban me",
                    "created_at": "2026-06-20T11:00:00Z"
                }
                """
            )
        );

        UnbanRequestCreatedEvent published = bus.EventsOf<UnbanRequestCreatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RequestId.Should().Be("60");
        published.UserId.Should().Be("1339");
        published.UserDisplayName.Should().Be("Not_Cool_User");
        published.UserLogin.Should().Be("not_cool_user");
        published.Text.Should().Be("unban me");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelUnbanRequestResolve_PublishesUnbanRequestResolvedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelUnbanRequestResolveTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.unban_request.resolve",
                """
                {
                    "id": "60",
                    "user_id": "1339",
                    "user_login": "not_cool_user",
                    "user_name": "Not_Cool_User",
                    "moderator_user_id": "1337",
                    "moderator_user_name": "Cool_User",
                    "resolution_text": "no",
                    "status": "denied"
                }
                """
            )
        );

        UnbanRequestResolvedEvent published = bus.EventsOf<UnbanRequestResolvedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.RequestId.Should().Be("60");
        published.UserId.Should().Be("1339");
        published.UserDisplayName.Should().Be("Not_Cool_User");
        published.ModeratorId.Should().Be("1337");
        published.ModeratorDisplayName.Should().Be("Cool_User");
        published.Status.Should().Be("denied");
        published.ResolutionText.Should().Be("no");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelModeratorAdd_PublishesModeratorAddedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModeratorAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.moderator.add",
                """
                {
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev"
                }
                """
            )
        );

        ModeratorAddedEvent published = bus.EventsOf<ModeratorAddedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("141981764");
        published.UserLogin.Should().Be("twitchdev");
        published.UserDisplayName.Should().Be("TwitchDev");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelModeratorRemove_PublishesModeratorRemovedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelModeratorRemoveTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.moderator.remove",
                """
                {
                    "user_id": "141981764",
                    "user_login": "twitchdev",
                    "user_name": "TwitchDev"
                }
                """
            )
        );

        ModeratorRemovedEvent published = bus.EventsOf<ModeratorRemovedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("141981764");
        published.UserLogin.Should().Be("twitchdev");
        published.UserDisplayName.Should().Be("TwitchDev");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelVipAdd_PublishesVipAddedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelVipAddTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.vip.add",
                """
                {
                    "user_id": "1234",
                    "user_login": "mod_user",
                    "user_name": "Mod_User"
                }
                """
            )
        );

        VipAddedEvent published = bus.EventsOf<VipAddedEvent>().Should().ContainSingle().Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserLogin.Should().Be("mod_user");
        published.UserDisplayName.Should().Be("Mod_User");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task ChannelVipRemove_PublishesVipRemovedEvent()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        ChannelVipRemoveTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "channel.vip.remove",
                """
                {
                    "user_id": "1234",
                    "user_login": "mod_user",
                    "user_name": "Mod_User"
                }
                """
            )
        );

        VipRemovedEvent published = bus.EventsOf<VipRemovedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("1234");
        published.UserLogin.Should().Be("mod_user");
        published.UserDisplayName.Should().Be("Mod_User");
        published.OccurredAt.Should().Be(Clock.GetUtcNow());
    }
}
