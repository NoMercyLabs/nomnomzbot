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
/// Behaviour tests for the four AutoMod translators. Each proves the consequence of translating a realistic raw
/// EventSub payload: the matching strongly-typed domain event is published carrying the parsed fields, the resolved
/// tenant, and the injected (deterministic) clock — including the v2 fragment-concatenation path, the per-category
/// settings levels, and the terms list.
/// </summary>
public sealed class AutoModTranslatorsTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventSubNotification Notification(Guid tenant, string type, string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = "msg-automod-1",
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "2",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "broadcaster-99",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task Translate_AutoModMessageHold_PublishesHeldEvent_ConcatenatingFragments()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        AutoModMessageHoldTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "automod.message.hold",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "user_id": "4242",
                    "user_login": "rude_user",
                    "user_name": "Rude_User",
                    "message_id": "held-msg-1",
                    "message": {
                        "text": "",
                        "fragments": [
                            { "type": "text", "text": "you are ", "cheermote": null, "emote": null },
                            { "type": "text", "text": "such a problem", "cheermote": null, "emote": null }
                        ]
                    },
                    "reason": "automod",
                    "automod": {
                        "category": "bullying",
                        "level": 4,
                        "boundaries": [{ "start_pos": 0, "end_pos": 20 }]
                    },
                    "blocked_term": null,
                    "held_at": "2026-06-20T11:29:30Z"
                }
                """
            )
        );

        AutoModMessageHeldEvent published = bus.EventsOf<AutoModMessageHeldEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant, "the dispatcher resolved the tenant");
        published.MessageId.Should().Be("held-msg-1");
        published.UserId.Should().Be("4242");
        published.UserLogin.Should().Be("rude_user");
        published.UserDisplayName.Should().Be("Rude_User");
        published
            .Text.Should()
            .Be("you are such a problem", "v2 fragments are concatenated into the plain text");
        published
            .Category.Should()
            .Be("bullying", "category is read from the nested automod object");
        published.Level.Should().Be(4, "level is read from the nested automod object");
        published
            .HeldAt.Should()
            .Be(new DateTimeOffset(2026, 6, 20, 11, 29, 30, TimeSpan.Zero), "held_at is parsed");
        published
            .Timestamp.Should()
            .Be(Clock.GetUtcNow(), "the publisher stamps the injected clock for determinism");
    }

    [Fact]
    public async Task Translate_AutoModMessageHold_PrefersTopLevelText_WhenPresent()
    {
        CapturingEventBus bus = new();
        AutoModMessageHoldTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "automod.message.hold",
                """
                {
                    "user_id": "1",
                    "user_login": "u",
                    "user_name": "U",
                    "message_id": "m",
                    "message": { "text": "flat text wins", "fragments": [] },
                    "automod": { "category": "swearing", "level": 2 }
                }
                """
            )
        );

        AutoModMessageHeldEvent published = bus.EventsOf<AutoModMessageHeldEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Text.Should().Be("flat text wins", "a present message.text is used verbatim");
        published.HeldAt.Should().Be(Clock.GetUtcNow(), "an absent held_at degrades to the clock");
    }

    [Fact]
    public async Task Translate_AutoModMessageUpdate_PublishesUpdatedEvent_WithStatusAndModerator()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        AutoModMessageUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "automod.message.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "moderator_user_id": "mod-7",
                    "moderator_user_login": "cool_mod",
                    "moderator_user_name": "Cool_Mod",
                    "user_id": "4242",
                    "user_login": "rude_user",
                    "user_name": "Rude_User",
                    "message_id": "held-msg-1",
                    "message": { "text": "you are such a problem", "fragments": [] },
                    "status": "denied",
                    "held_at": "2026-06-20T11:29:30Z"
                }
                """
            )
        );

        AutoModMessageUpdatedEvent published = bus.EventsOf<AutoModMessageUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.MessageId.Should().Be("held-msg-1");
        published.UserId.Should().Be("4242");
        published.UserDisplayName.Should().Be("Rude_User");
        published.ModeratorId.Should().Be("mod-7");
        published.ModeratorDisplayName.Should().Be("Cool_Mod");
        published.Status.Should().Be("denied", "the moderator's verdict is carried verbatim");
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task Translate_AutoModSettingsUpdate_PublishesSettingsEvent_WithCategoryLevels()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        AutoModSettingsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "automod.settings.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "moderator_user_id": "mod-7",
                    "moderator_user_login": "cool_mod",
                    "moderator_user_name": "Cool_Mod",
                    "overall_level": null,
                    "disability": 1,
                    "aggression": 2,
                    "sexuality_sex_or_gender": 3,
                    "misogyny": 4,
                    "bullying": 5,
                    "swearing": 0,
                    "race_ethnicity_or_religion": 6,
                    "sex_based_terms": 7
                }
                """
            )
        );

        AutoModSettingsUpdatedEvent published = bus.EventsOf<AutoModSettingsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ModeratorId.Should().Be("mod-7");
        published.ModeratorDisplayName.Should().Be("Cool_Mod");
        published
            .OverallLevel.Should()
            .BeNull("a JSON null overall_level means per-category levels are in use");
        published.Bullying.Should().Be(5);
        published.Aggression.Should().Be(2);
        published.Sexuality.Should().Be(3, "sexuality is read from sexuality_sex_or_gender");
        published.Disability.Should().Be(1);
        published.Misogyny.Should().Be(4);
        published.RaceEthnicityOrReligion.Should().Be(6);
        published.SexBasedTerms.Should().Be(7);
        published.Swearing.Should().Be(0);
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task Translate_AutoModSettingsUpdate_UnwrapsDataArray_WhenPresent()
    {
        CapturingEventBus bus = new();
        AutoModSettingsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "automod.settings.update",
                """
                {
                    "data": [
                        {
                            "moderator_user_id": "mod-9",
                            "moderator_user_name": "Wrapped_Mod",
                            "overall_level": 2,
                            "bullying": 2,
                            "aggression": 2,
                            "sexuality_sex_or_gender": 2,
                            "disability": 2,
                            "misogyny": 2,
                            "race_ethnicity_or_religion": 2,
                            "sex_based_terms": 2,
                            "swearing": 2
                        }
                    ]
                }
                """
            )
        );

        AutoModSettingsUpdatedEvent published = bus.EventsOf<AutoModSettingsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published
            .ModeratorDisplayName.Should()
            .Be("Wrapped_Mod", "settings are read out of the data[0] wrapper");
        published.OverallLevel.Should().Be(2, "an integer overall_level is parsed");
        published.Bullying.Should().Be(2);
    }

    [Fact]
    public async Task Translate_AutoModTermsUpdate_PublishesTermsEvent_WithTermList()
    {
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        AutoModTermsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                tenant,
                "automod.terms.update",
                """
                {
                    "broadcaster_user_id": "broadcaster-99",
                    "broadcaster_user_login": "streamer",
                    "broadcaster_user_name": "Streamer",
                    "moderator_user_id": "mod-7",
                    "moderator_user_login": "cool_mod",
                    "moderator_user_name": "Cool_Mod",
                    "action": "add_blocked",
                    "from_automod": true,
                    "terms": ["badword", "anotherword", "thirdword"]
                }
                """
            )
        );

        AutoModTermsUpdatedEvent published = bus.EventsOf<AutoModTermsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.BroadcasterId.Should().Be(tenant);
        published.ModeratorId.Should().Be("mod-7");
        published.ModeratorDisplayName.Should().Be("Cool_Mod");
        published.Action.Should().Be("add_blocked", "the raw Twitch action is carried verbatim");
        published.FromAutomod.Should().BeTrue("from_automod is parsed");
        published.Terms.Should().Equal("badword", "anotherword", "thirdword").And.HaveCount(3);
        published.Timestamp.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task Translate_AutoModTermsUpdate_MissingTerms_DegradesToEmptyList()
    {
        CapturingEventBus bus = new();
        AutoModTermsUpdateTranslator translator = new(bus, Clock);

        await translator.TranslateAsync(
            Notification(
                Guid.NewGuid(),
                "automod.terms.update",
                """
                {
                    "moderator_user_id": "mod-7",
                    "moderator_user_name": "Cool_Mod",
                    "action": "remove_permitted",
                    "from_automod": false
                }
                """
            )
        );

        AutoModTermsUpdatedEvent published = bus.EventsOf<AutoModTermsUpdatedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        published.Action.Should().Be("remove_permitted");
        published.FromAutomod.Should().BeFalse();
        published.Terms.Should().BeEmpty("an absent terms array degrades to empty, never throws");
    }
}
