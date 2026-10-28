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
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// The Music Player migration tells the streamer what it cannot do for them: add the Music Player to OBS, and
/// look at custom widgets that play audio or read Spotify directly. Every item is derived on read, so it clears
/// the moment its condition clears.
/// </summary>
public sealed class MusicPlayerMigrationSourceTests
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid OtherChannel = Guid.Parse("0192b000-0000-7000-8000-000000000c02");
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePresence : IOverlayPresenceRegistry
    {
        public bool MusicConnected { get; set; }

        public bool IsWidgetAttached(Guid broadcasterId, Guid widgetId) => false;

        public bool IsOverlayConnected(Guid broadcasterId) => false;

        public string? GetAudioTarget(Guid broadcasterId) => null;

        public bool IsAudioSourceConnected(Guid broadcasterId) => false;

        public bool IsMusicSourceConnected(Guid broadcasterId) =>
            broadcasterId == Channel && MusicConnected;
    }

    private static readonly FakePresence Presence = new();

    private static ActionRequiredInboxServiceTestDbContext NewDb()
    {
        Presence.MusicConnected = false;
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        foreach (
            (Guid id, string name, string twitchId) in new[]
            {
                (Channel, "streamer", "100"),
                (OtherChannel, "other", "200"),
            }
        )
            db.Channels.Add(
                new()
                {
                    Id = id,
                    Name = name,
                    NameNormalized = name,
                    TwitchChannelId = twitchId,
                }
            );

        db.SaveChanges();
        return db;
    }

    private static Widget AddWidget(
        ActionRequiredInboxServiceTestDbContext db,
        Guid channelId,
        string name,
        string source = "custom",
        bool enableAudio = false,
        string? code = null,
        bool deleted = false
    )
    {
        Widget widget = new()
        {
            BroadcasterId = channelId,
            Name = name,
            Source = source,
            Settings = enableAudio ? new() { ["enableAudio"] = true } : new(),
            DeletedAt = deleted ? Now.UtcDateTime : null,
        };
        db.Widgets.Add(widget);
        if (code is not null)
        {
            WidgetVersion version = new()
            {
                WidgetId = widget.Id,
                BroadcasterId = channelId,
                VersionNumber = 1,
                SourceCode = code,
                BuildStatus = "success",
                CreatedAt = Now.UtcDateTime,
            };
            db.WidgetVersions.Add(version);
            widget.ActiveVersionId = version.Id;
        }

        db.SaveChanges();
        return widget;
    }

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        ActionRequiredInboxServiceTestDbContext db,
        Guid? channelId = null,
        params string[] dismissed
    )
    {
        MusicPlayerMigrationSource source = new(db, Presence, new FakeTimeProvider(Now));
        Result<List<ActionRequiredItemDto>> result = await source.GetItemsAsync(
            channelId ?? Channel,
            new HashSet<string>(dismissed)
        );
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    [Fact]
    public async Task A_music_player_row_with_no_attached_page_asks_the_streamer_to_add_it_to_obs()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Music Player", source: "first_party");

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Id.Should().Be("music-player-missing");
        item.Kind.Should().Be("music_player_missing");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_music_player_missing_title");
        item.MessageKey.Should().Be("attention_music_player_missing_message");
        item.DeepLinkRoute.Should().Be("overlays");
        item.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task The_music_player_item_clears_once_a_page_attaches()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Music Player", source: "first_party");
        (await ItemsAsync(db)).Should().ContainSingle();

        Presence.MusicConnected = true;

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_channel_without_a_music_player_row_gets_no_music_player_item()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_deleted_music_player_row_raises_nothing()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Music Player", source: "first_party", deleted: true);

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task Custom_widgets_with_audio_on_are_listed_once_by_name()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Zeta jukebox", enableAudio: true);
        AddWidget(db, Channel, "Alpha jukebox", enableAudio: true);
        AddWidget(db, Channel, "Silent goal");

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Kind.Should().Be("custom_widget_audio");
        item.Id.Should().StartWith("music-audio-widgets:");
        item.Severity.Should().Be("info");
        item.TitleKey.Should().Be("attention_custom_widget_audio_title");
        item.MessageKey.Should().Be("attention_custom_widget_audio_message");
        item.Parameters["widgetNames"].Should().Be("Alpha jukebox, Zeta jukebox");
        item.Count.Should().Be(2);
        item.DeepLinkRoute.Should().Be("overlays");
    }

    [Fact]
    public async Task Audio_on_a_first_party_widget_is_not_a_custom_widget_report()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Bot Audio", source: "first_party", enableAudio: true);

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task Custom_widgets_that_read_spotify_directly_are_reported_and_not_edited()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        Widget reader = AddWidget(
            db,
            Channel,
            "Spotify bar",
            code: "fetch('https://api.spotify.com/v1/me/player')"
        );
        AddWidget(db, Channel, "Plain", code: "render('hi')");

        ActionRequiredItemDto item = (await ItemsAsync(db)).Should().ContainSingle().Subject;

        item.Kind.Should().Be("custom_widget_spotify");
        item.Id.Should().StartWith("music-spotify-widgets:");
        item.TitleKey.Should().Be("attention_custom_widget_spotify_title");
        item.MessageKey.Should().Be("attention_custom_widget_spotify_message");
        item.Parameters["widgetNames"].Should().Be("Spotify bar");
        db.WidgetVersions.Single(v => v.WidgetId == reader.Id)
            .SourceCode.Should()
            .Be("fetch('https://api.spotify.com/v1/me/player')");
    }

    [Fact]
    public async Task Only_the_active_version_counts_and_the_item_clears_when_the_code_stops_reading_spotify()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        Widget widget = AddWidget(db, Channel, "Spotify bar", code: "api.spotify.com");
        (await ItemsAsync(db)).Should().ContainSingle();

        WidgetVersion clean = new()
        {
            WidgetId = widget.Id,
            BroadcasterId = Channel,
            VersionNumber = 2,
            SourceCode = "nnz.music.onChange(draw)",
            BuildStatus = "success",
            CreatedAt = Now.UtcDateTime,
        };
        db.WidgetVersions.Add(clean);
        widget.ActiveVersionId = clean.Id;
        db.SaveChanges();

        (await ItemsAsync(db)).Should().BeEmpty();
    }

    [Fact]
    public async Task Items_are_scoped_to_the_channel_asked_about()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, OtherChannel, "Music Player", source: "first_party");
        AddWidget(db, OtherChannel, "Other jukebox", enableAudio: true);

        (await ItemsAsync(db)).Should().BeEmpty();
        (await ItemsAsync(db, OtherChannel)).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_dismissed_list_resurfaces_when_its_members_change()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Alpha jukebox", enableAudio: true);
        string dismissed = (await ItemsAsync(db)).Single().Id;

        (await ItemsAsync(db, null, dismissed)).Should().BeEmpty();

        AddWidget(db, Channel, "Beta jukebox", enableAudio: true);

        ActionRequiredItemDto again = (await ItemsAsync(db, null, dismissed))
            .Should()
            .ContainSingle()
            .Subject;
        again.Id.Should().NotBe(dismissed);
    }

    [Fact]
    public async Task Each_item_dismisses_under_its_own_key_and_the_prefixes_cover_every_id()
    {
        ActionRequiredInboxServiceTestDbContext db = NewDb();
        AddWidget(db, Channel, "Music Player", source: "first_party");
        AddWidget(db, Channel, "Jukebox", enableAudio: true, code: "api.spotify.com");
        MusicPlayerMigrationSource source = new(db, Presence, new FakeTimeProvider(Now));

        List<ActionRequiredItemDto> items = await ItemsAsync(db);

        items.Should().HaveCount(3);
        foreach (ActionRequiredItemDto item in items)
        {
            Result<List<string>> keys = await source.ResolveDismissalKeysAsync(Channel, item.Id);
            keys.Value.Should().Equal(item.Id);
            source
                .KeyPrefixes.Any(p => item.Id.StartsWith(p, StringComparison.Ordinal))
                .Should()
                .BeTrue();
        }
    }
}
