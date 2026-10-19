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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// music-playback-determinism.md step 1 + 3: every channel gets one <c>music_player</c> widget row and the
/// first-party Audio widget loses the old "TTS Audio" name, but only where the streamer never renamed it.
/// </summary>
public sealed class MusicPlayerMigrationServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero)
    );
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static MusicPlayerMigrationService NewMigration(WidgetTestDbContext db)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("BUNDLE", "hash", "")));
        WidgetService widgets = new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            build,
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IPlaybackStateReader>()
        );
        return new(db, widgets, NullLogger<MusicPlayerMigrationService>.Instance);
    }

    private static async Task<Guid> SeedChannelAsync(WidgetSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = channelId.ToString("N")[..8],
                Name = "channel" + channelId.ToString("N")[..6],
                NameNormalized = "channel" + channelId.ToString("N")[..6],
                OverlayToken = "tok" + channelId.ToString("N")[..6],
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Dictionary<string, Guid>> SeedGalleryAsync(
        WidgetSqliteTestDatabase database
    )
    {
        Dictionary<string, Guid> ids = [];
        await using WidgetTestDbContext db = database.NewContext();
        foreach (
            (string key, string name) in new[]
            {
                ("audio", "Bot Audio"),
                ("music_player", "Music Player"),
            }
        )
        {
            Guid id = Guid.CreateVersion7();
            ids[key] = id;
            db.WidgetGalleryItems.Add(
                new()
                {
                    Id = id,
                    Name = name,
                    Description = name,
                    Framework = "vue",
                    TrustTier = "first_party",
                    SourceKind = "in_repo",
                    NaturalKey = key,
                    SourceCode = "SOURCE",
                    ReviewStatus = "verified",
                    AvailableInSaaS = true,
                    DefaultEventSubscriptions = [],
                    DefaultSettings = new(),
                }
            );
        }

        await db.SaveChangesAsync();
        return ids;
    }

    private static async Task SeedAudioRowAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        Guid audioGalleryItem,
        string name
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Widgets.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = channelId,
                Name = name,
                Framework = "vue",
                Source = "first_party",
                GalleryItemId = audioGalleryItem,
                IsEnabled = true,
                Settings = new(),
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<List<(string Name, string? Key)>> RowsAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        List<Widget> widgets = await db
            .Widgets.Where(w => w.BroadcasterId == channelId)
            .OrderBy(w => w.Name)
            .ToListAsync();
        List<WidgetGalleryItem> items = await db.WidgetGalleryItems.ToListAsync();
        return
        [
            .. widgets.Select(w =>
                (w.Name, items.SingleOrDefault(i => i.Id == w.GalleryItemId)?.NaturalKey)
            ),
        ];
    }

    private static async Task<MusicPlayerMigrationReport> RunAsync(
        WidgetSqliteTestDatabase database
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        return await NewMigration(db).MigrateAllChannelsAsync();
    }

    [Fact]
    public async Task A_fresh_channel_gets_exactly_one_music_player_row_and_a_second_run_changes_nothing()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedGalleryAsync(database);
        Guid channel = await SeedChannelAsync(database);

        MusicPlayerMigrationReport first = await RunAsync(database);
        MusicPlayerMigrationReport second = await RunAsync(database);

        first.Should().Be(new MusicPlayerMigrationReport(1, 1, 0));
        second.Should().Be(new MusicPlayerMigrationReport(1, 0, 0));
        (await RowsAsync(database, channel)).Should().Equal([("Music Player", "music_player")]);
    }

    [Fact]
    public async Task A_channel_with_only_the_old_audio_row_gets_a_music_player_and_the_audio_row_is_renamed()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Dictionary<string, Guid> gallery = await SeedGalleryAsync(database);
        Guid channel = await SeedChannelAsync(database);
        await SeedAudioRowAsync(database, channel, gallery["audio"], "TTS Audio");

        MusicPlayerMigrationReport report = await RunAsync(database);
        MusicPlayerMigrationReport again = await RunAsync(database);

        report.Should().Be(new MusicPlayerMigrationReport(1, 1, 1));
        again.Should().Be(new MusicPlayerMigrationReport(1, 0, 0));
        (await RowsAsync(database, channel))
            .Should()
            .Equal([("Bot Audio", "audio"), ("Music Player", "music_player")]);
    }

    [Fact]
    public async Task An_audio_row_the_streamer_renamed_keeps_its_name()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Dictionary<string, Guid> gallery = await SeedGalleryAsync(database);
        Guid channel = await SeedChannelAsync(database);
        await SeedAudioRowAsync(database, channel, gallery["audio"], "My Sound Stack");

        MusicPlayerMigrationReport report = await RunAsync(database);

        report.Should().Be(new MusicPlayerMigrationReport(1, 1, 0));
        (await RowsAsync(database, channel))
            .Should()
            .Equal([("Music Player", "music_player"), ("My Sound Stack", "audio")]);
    }

    [Fact]
    public async Task An_already_migrated_channel_is_left_exactly_as_it_is()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Dictionary<string, Guid> gallery = await SeedGalleryAsync(database);
        Guid channel = await SeedChannelAsync(database);
        await SeedAudioRowAsync(database, channel, gallery["audio"], "Bot Audio");
        await RunAsync(database);
        List<(string Name, string? Key)> before = await RowsAsync(database, channel);

        MusicPlayerMigrationReport report = await RunAsync(database);

        report.Should().Be(new MusicPlayerMigrationReport(1, 0, 0));
        (await RowsAsync(database, channel)).Should().Equal(before);
        before.Should().Equal([("Bot Audio", "audio"), ("Music Player", "music_player")]);
    }

    [Fact]
    public async Task The_hosted_service_runs_the_migration_only_when_the_lease_is_granted()
    {
        IMusicPlayerMigrationService migration = Substitute.For<IMusicPlayerMigrationService>();
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(migration)
            .AddSingleton(guard)
            .BuildServiceProvider();
        MusicPlayerMigrationHostedService hosted = new(
            provider.GetRequiredService<IServiceScopeFactory>()
        );

        guard
            .TryAcquireAsync(
                MusicPlayerMigrationHostedService.LeaseResourceName,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns((IAsyncDisposable?)null);
        await hosted.StartAsync(CancellationToken.None);
        await migration.DidNotReceive().MigrateAllChannelsAsync(Arg.Any<CancellationToken>());

        guard
            .TryAcquireAsync(
                MusicPlayerMigrationHostedService.LeaseResourceName,
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Substitute.For<IAsyncDisposable>());
        await hosted.StartAsync(CancellationToken.None);
        await migration.Received(1).MigrateAllChannelsAsync(Arg.Any<CancellationToken>());
    }
}
