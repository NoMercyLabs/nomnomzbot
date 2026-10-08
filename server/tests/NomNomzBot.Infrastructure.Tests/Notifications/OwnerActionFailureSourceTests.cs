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
        string error
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
                $"{{\"ActionType\":\"{actionType}\",\"Caller\":\"BSOD\",\"Source\":\"widget\",\"Error\":\"{error}\"}}",
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

        (await ItemsAsync(db)).Should().ContainSingle().Which.DeepLinkRoute.Should().Be("widgets");
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

    [Fact]
    public async Task A_failure_older_than_24_hours_is_gone()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.Add(Failure(Channel, 1, T0.AddHours(-23), "obs_input_mute", "stale"));
        await db.SaveChangesAsync();

        (await ItemsAsync(db)).Should().BeEmpty();
    }
}
