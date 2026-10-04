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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// A channel with overlay pages open but no Audio Source page hears its sound and TTS from some other page.
/// The inbox must say so, and the item must be gone once an Audio Source page is connected.
/// </summary>
public sealed class AudioSourceMissingSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000b01");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePresence(bool overlay, bool audioSource) : IOverlayPresenceRegistry
    {
        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => overlay;

        public bool IsOverlayConnected(Guid broadcasterId) => broadcasterId == Channel && overlay;

        public string? GetAudioTarget(Guid broadcasterId) => overlay ? "conn" : null;

        public bool IsAudioSourceConnected(Guid broadcasterId) =>
            broadcasterId == Channel && audioSource;
    }

    private static readonly DateTimeOffset Started = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);

    private static AudioSourceMissingSource NewSource(
        bool overlay,
        bool audioSource,
        bool live,
        params (string Id, DateTimeOffset StartedAt, bool Open)[] streams
    )
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "streamer",
                NameNormalized = "streamer",
                TwitchChannelId = "100",
                IsLive = live,
            }
        );
        foreach ((string id, DateTimeOffset startedAt, bool open) in streams)
            db.Streams.Add(
                new()
                {
                    Id = id,
                    ChannelId = Channel,
                    StartedAt = startedAt,
                    EndedAt = open ? null : startedAt.AddHours(1),
                }
            );
        db.SaveChanges();
        return new(db, new FakePresence(overlay, audioSource), new FakeTimeProvider(Now));
    }

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        bool overlay,
        bool audioSource,
        bool live = false,
        string[]? dismissed = null,
        (string Id, DateTimeOffset StartedAt, bool Open)[]? streams = null
    )
    {
        AudioSourceMissingSource source = NewSource(
            overlay,
            audioSource,
            live,
            streams ?? (live ? [("stream-1", Started, true)] : [])
        );
        Result<List<ActionRequiredItemDto>> result = await source.GetItemsAsync(
            Channel,
            new HashSet<string>(dismissed ?? [])
        );
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task An_overlay_page_without_an_audio_source_raises_one_item()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(overlay: true, audioSource: false);

        ActionRequiredItemDto item = items.Should().ContainSingle().Subject;
        item.Id.Should().Be("audio-source-missing");
        item.Kind.Should().Be("audio_source_missing");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_audio_source_missing_title");
        item.MessageKey.Should().Be("attention_audio_source_missing_message");
        item.DeepLinkRoute.Should().Be("widgets");
        item.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_live_channel_without_an_audio_page_raises_one_critical_item_even_with_no_overlay_open()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(
            overlay: false,
            audioSource: false,
            live: true
        );

        ActionRequiredItemDto item = items.Should().ContainSingle().Subject;
        item.Id.Should().Be("audio-source-missing:live:stream-1");
        item.Kind.Should().Be("audio_source_missing");
        item.Severity.Should().Be("critical");
        item.TitleKey.Should().Be("attention_audio_source_missing_live_title");
        item.MessageKey.Should().Be("attention_audio_source_missing_live_message");
        item.DeepLinkRoute.Should().Be("tts");
        item.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task A_live_channel_with_a_connected_audio_page_raises_nothing()
    {
        (await ItemsAsync(overlay: true, audioSource: true, live: true)).Should().BeEmpty();
    }

    [Fact]
    public void The_live_flip_signals_the_inbox_from_the_stream_handlers_not_from_journal_events()
    {
        AudioSourceMissingSource source = NewSource(false, false, true);

        source.InvalidatingEventTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_connected_audio_source_raises_nothing()
    {
        (await ItemsAsync(overlay: true, audioSource: true)).Should().BeEmpty();
    }

    [Fact]
    public async Task No_overlay_page_at_all_raises_nothing()
    {
        (await ItemsAsync(overlay: false, audioSource: false)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_dismissed_item_is_not_raised_again()
    {
        (
            await ItemsAsync(
                overlay: true,
                audioSource: false,
                live: false,
                dismissed: ["audio-source-missing"]
            )
        )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task The_item_dismisses_under_its_own_key()
    {
        AudioSourceMissingSource source = NewSource(true, false, false);

        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            "audio-source-missing"
        );

        keys.Value.Should().Equal("audio-source-missing");
        source.KeyPrefixes.Should().Contain("audio-source-missing");
    }

    [Fact]
    public async Task The_live_item_is_keyed_to_the_newest_open_stream()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(
            overlay: false,
            audioSource: false,
            live: true,
            streams:
            [
                ("older-open", Started, true),
                ("newest-open", Started.AddHours(2), true),
                ("closed-newest", Started.AddHours(5), false),
            ]
        );

        items
            .Should()
            .ContainSingle()
            .Subject.Id.Should()
            .Be("audio-source-missing:live:newest-open");
    }

    [Fact]
    public async Task A_dismissed_offline_warning_does_not_hide_the_live_item()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(
            overlay: false,
            audioSource: false,
            live: true,
            dismissed: ["audio-source-missing"]
        );

        items.Should().ContainSingle().Subject.Id.Should().Be("audio-source-missing:live:stream-1");
    }

    [Fact]
    public async Task A_dismissed_live_item_hides_only_that_streams_item()
    {
        (
            await ItemsAsync(
                overlay: false,
                audioSource: false,
                live: true,
                dismissed: ["audio-source-missing:live:stream-1"]
            )
        )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task A_live_dismissal_of_an_older_stream_does_not_hide_the_new_streams_item()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(
            overlay: false,
            audioSource: false,
            live: true,
            dismissed: ["audio-source-missing:live:stream-1"],
            streams: [("stream-1", Started, false), ("stream-2", Started.AddDays(1), true)]
        );

        items.Should().ContainSingle().Subject.Id.Should().Be("audio-source-missing:live:stream-2");
    }

    [Fact]
    public async Task A_dismissed_live_item_does_not_hide_the_offline_warning_later()
    {
        List<ActionRequiredItemDto> items = await ItemsAsync(
            overlay: true,
            audioSource: false,
            live: false,
            dismissed: ["audio-source-missing:live:stream-1"]
        );

        items.Should().ContainSingle().Subject.Id.Should().Be("audio-source-missing");
    }

    [Fact]
    public async Task The_live_item_dismisses_under_its_own_key_and_the_prefix_covers_both_ids()
    {
        AudioSourceMissingSource source = NewSource(
            false,
            false,
            true,
            ("stream-1", Started, true)
        );

        Result<List<string>> keys = await source.ResolveDismissalKeysAsync(
            Channel,
            "audio-source-missing:live:stream-1"
        );

        keys.Value.Should().Equal("audio-source-missing:live:stream-1");
        "audio-source-missing:live:stream-1"
            .StartsWith(source.KeyPrefixes.Single(), StringComparison.Ordinal)
            .Should()
            .BeTrue();
    }
}
