// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// A widget or script action that failed (a BSOD mute that never reached OBS) must reach the streamer's inbox
/// with the action and the reason, and link to the page where it is fixed. A failure is gone once dismissed or
/// after a day, and another channel's failures never show.
/// </summary>
public sealed class OwnerActionFailureSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-000000000c02");
    private static readonly DateTime T0 = new(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc);

    private static EventJournal Failure(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string actionType,
        string error,
        string? errorCode = null
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = "OwnerActionFailedEvent",
            EventVersion = 1,
            Source = "domain",
            Payload =
                $"{{\"ActionType\":\"{actionType}\",\"Caller\":\"BSOD\",\"Source\":\"widget\",\"Error\":\"{error}\",\"ErrorCode\":{(errorCode is null ? "null" : $"\"{errorCode}\"")}}}",
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        ActionRequiredInboxServiceTestDbContext db,
        string[]? dismissed = null
    )
    {
        OwnerActionFailureSource source = new(db, new FakeTimeProvider(T0.AddHours(2)));
        Result<List<ActionRequiredItemDto>> result = await source.GetItemsAsync(
            Channel,
            new HashSet<string>(dismissed ?? [])
        );
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task One_failed_obs_action_is_one_warning_naming_the_action_and_the_error_linked_to_obs()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        EventJournal mine = Failure(Channel, 1, T0, "obs_input_mute", "OBS bridge did not answer");
        db.EventJournals.AddRange(
            mine,
            Failure(OtherChannel, 1, T0, "obs_input_mute", "other channel")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Id.Should().Be($"owner-action-failed:{mine.EventId}");
        item.Kind.Should().Be("owner_action_failed");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_owner_action_failed_title");
        item.MessageKey.Should().Be("attention_owner_action_failed_message");
        item.Parameters.Should()
            .Contain("action", "obs_input_mute")
            .And.Contain("error", "OBS bridge did not answer");
        item.DeepLinkRoute.Should().Be("obs");
        item.DetectedAt.Should().Be(T0);
    }

    [Fact]
    public async Task A_failed_action_that_is_not_an_obs_action_links_to_the_widgets_page()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.Add(Failure(Channel, 1, T0, "tts_synthesize", "provider is down"));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().ContainSingle().Which.DeepLinkRoute.Should().Be("overlays");
    }

    [Fact]
    public async Task A_dismissed_failure_is_gone_and_a_later_one_still_shows()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        EventJournal first = Failure(Channel, 1, T0, "obs_input_mute", "first");
        EventJournal second = Failure(Channel, 2, T0.AddMinutes(5), "obs_input_mute", "second");
        db.EventJournals.AddRange(first, second);
        await db.SaveChangesAsync();

        List<ActionRequiredItemDto> items = await ItemsAsync(
            db,
            [$"owner-action-failed:{first.EventId}"]
        );

        items.Should().ContainSingle().Which.Parameters.Should().Contain("error", "second");
    }

    private static EventJournal ObsDown(Guid channelId, long position, DateTime occurredAt) =>
        Failure(
            channelId,
            position,
            occurredAt,
            "obs_scene_switch",
            "No OBS bridge is connected for this channel.",
            "OBS_BRIDGE_OFFLINE"
        );

    private static EventJournal Reconnected(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string eventType,
        string payload
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = eventType,
            EventVersion = 1,
            Source = "domain",
            Payload = payload,
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };

    [Fact]
    public async Task Three_failures_while_obs_is_not_connected_are_one_notice_with_count_3_linked_to_obs()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            ObsDown(Channel, 1, T0),
            ObsDown(Channel, 2, T0.AddMinutes(1)),
            ObsDown(Channel, 3, T0.AddMinutes(2)),
            ObsDown(OtherChannel, 1, T0)
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Id.Should().Be("owner-action-failed:obs-not-connected");
        item.Kind.Should().Be("obs_not_connected");
        item.TitleKey.Should().Be("attention_obs_not_connected_title");
        item.MessageKey.Should().Be("attention_obs_not_connected_message");
        item.Count.Should().Be(3);
        item.DeepLinkRoute.Should().Be("obs");
        item.DetectedAt.Should().Be(T0.AddMinutes(2));
    }

    [Fact]
    public async Task A_failure_that_is_not_about_a_missing_obs_connection_stays_its_own_warning_beside_the_notice()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            ObsDown(Channel, 1, T0),
            ObsDown(Channel, 2, T0.AddMinutes(1)),
            Failure(Channel, 3, T0.AddMinutes(2), "obs_input_mute", "Input not found")
        );
        await db.SaveChangesAsync();

        List<ActionRequiredItemDto> items = await ItemsAsync(db);

        items
            .Select(i => i.Kind)
            .Should()
            .BeEquivalentTo("obs_not_connected", "owner_action_failed");
        items.Single(i => i.Kind == "obs_not_connected").Count.Should().Be(2);
    }

    [Theory]
    [InlineData("ObsConnectionEstablishedEvent", "{\"Streaming\":false,\"Recording\":false}")]
    [InlineData("ObsBridgeStateChangedEvent", "{\"InstanceCount\":1,\"HasLeader\":true}")]
    public async Task The_notice_clears_when_obs_connects_again_and_a_later_failure_brings_it_back(
        string eventType,
        string payload
    )
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            ObsDown(Channel, 1, T0),
            ObsDown(Channel, 2, T0.AddMinutes(1)),
            Reconnected(Channel, 3, T0.AddMinutes(5), eventType, payload)
        );
        await db.SaveChangesAsync();
        (await ItemsAsync(db)).Should().BeEmpty();

        db.EventJournals.Add(ObsDown(Channel, 4, T0.AddMinutes(9)));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().ContainSingle().Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_bridge_that_left_does_not_count_as_a_reconnect()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            ObsDown(Channel, 1, T0),
            Reconnected(
                Channel,
                2,
                T0.AddMinutes(5),
                "ObsBridgeStateChangedEvent",
                "{\"InstanceCount\":0,\"HasLeader\":false}"
            )
        );
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().ContainSingle().Which.Count.Should().Be(1);
    }

    [Fact]
    public async Task Dismissing_the_notice_dismisses_every_failure_it_folded_and_a_new_one_shows_again()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        EventJournal first = ObsDown(Channel, 1, T0);
        EventJournal second = ObsDown(Channel, 2, T0.AddMinutes(1));
        db.EventJournals.AddRange(first, second);
        await db.SaveChangesAsync();
        OwnerActionFailureSource source = new(db, new FakeTimeProvider(T0.AddHours(2)));

        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            "owner-action-failed:obs-not-connected"
        );

        keys.Value.Should()
            .BeEquivalentTo(
                $"owner-action-failed:{first.EventId}",
                $"owner-action-failed:{second.EventId}"
            );
        db.EventJournals.Add(ObsDown(Channel, 3, T0.AddMinutes(30)));
        await db.SaveChangesAsync();
        (await ItemsAsync(db, [.. keys.Value])).Should().ContainSingle().Which.Count.Should().Be(1);
    }

    [Fact]
    public void A_connect_signal_refreshes_the_inbox_so_the_notice_clears_live()
    {
        OwnerActionFailureSource source = new(
            ActionRequiredInboxServiceTestDbContext.New(),
            new FakeTimeProvider(T0)
        );

        source
            .InvalidatingEventTypes.Should()
            .Contain([
                "OwnerActionFailedEvent",
                "ObsConnectionEstablishedEvent",
                "ObsBridgeStateChangedEvent",
            ]);
    }

    [Fact]
    public async Task A_failure_older_than_24_hours_is_gone()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.Add(Failure(Channel, 1, T0.AddHours(-23), "obs_input_mute", "stale"));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().BeEmpty();
    }
}
