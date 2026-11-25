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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The channel's <c>overlay.themeColor</c> reaches every widget as the <c>globalAccentColor</c> setting, in the
/// overlay manifest and in the join settings. A channel with no color, or an empty one, gets no such setting, so
/// a widget falls back to the theme's own color.
/// </summary>
public sealed class WidgetServiceGlobalAccentTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();
    private static readonly Guid OtherChannelId = Guid.CreateVersion7();
    private static readonly Guid WidgetId = Guid.CreateVersion7();

    [Fact]
    public async Task A_channel_with_a_theme_color_hands_it_to_the_join_settings_as_globalAccentColor()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, themeColor: "#12ab34");
        await using WidgetTestDbContext db = database.NewContext();

        Result<Dictionary<string, object>> result = await NewService(db)
            .GetEffectiveSettingsAsync(ChannelId, WidgetId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value["globalAccentColor"].Should().Be("#12ab34");
        result.Value["rewardId"].Should().Be("abc", "the saved settings stay beside it");
    }

    [Fact]
    public async Task A_channel_with_a_theme_color_hands_it_to_the_manifest_entry()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, themeColor: "#12ab34");
        await using WidgetTestDbContext db = database.NewContext();

        Result<OverlayManifest> result = await NewService(db).GetOverlayManifestAsync("tok");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Widgets.Single().Settings["globalAccentColor"].Should().Be("#12ab34");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_channel_with_no_or_an_empty_theme_color_gets_no_globalAccentColor(
        string? themeColor
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, themeColor);
        await using WidgetTestDbContext db = database.NewContext();

        Result<Dictionary<string, object>> join = await NewService(db)
            .GetEffectiveSettingsAsync(ChannelId, WidgetId);
        Result<OverlayManifest> manifest = await NewService(db).GetOverlayManifestAsync("tok");

        join.Value.Should().BeEquivalentTo(new Dictionary<string, object> { ["rewardId"] = "abc" });
        manifest.Value.Widgets.Single().Settings.Keys.Should().NotContain("globalAccentColor");
    }

    [Fact]
    public async Task Another_channels_theme_color_never_reaches_this_channels_widgets()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, themeColor: null, otherChannelColor: "#ff0000");
        await using WidgetTestDbContext db = database.NewContext();

        Result<Dictionary<string, object>> join = await NewService(db)
            .GetEffectiveSettingsAsync(ChannelId, WidgetId);

        join.Value.Keys.Should().NotContain("globalAccentColor");
    }

    private static WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            Substitute.For<IWidgetBuildService>(),
            new WidgetSettingsSchemaProvider(),
            new FakeTimeProvider(),
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IPlaybackStateReader>()
        );

    private static async Task SeedAsync(
        WidgetSqliteTestDatabase database,
        string? themeColor,
        string? otherChannelColor = null
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        foreach (
            (Guid id, string name, string token) in new[]
            {
                (ChannelId, "teststreamer", "tok"),
                (OtherChannelId, "otherstreamer", "other-tok"),
            }
        )
            db.Channels.Add(
                new()
                {
                    Id = id,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = id.ToString("N")[..8],
                    Name = name,
                    NameNormalized = name,
                    OverlayToken = token,
                }
            );

        if (themeColor is not null)
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = ChannelId,
                    Key = "overlay.themeColor",
                    Value = themeColor,
                }
            );
        if (otherChannelColor is not null)
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = OtherChannelId,
                    Key = "overlay.themeColor",
                    Value = otherChannelColor,
                }
            );

        Guid versionId = Guid.CreateVersion7();
        db.WidgetVersions.Add(
            new()
            {
                Id = versionId,
                WidgetId = WidgetId,
                BroadcasterId = ChannelId,
                VersionNumber = 1,
                FilesJson = ProjectJson.SerializeFiles(
                    new Dictionary<string, string> { ["index.ts"] = "export {}" }
                ),
                BuildStatus = "success",
                ContentHash = "hash123",
            }
        );
        db.Widgets.Add(
            new()
            {
                Id = WidgetId,
                BroadcasterId = ChannelId,
                Name = "Accent",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
                Settings = new() { ["rewardId"] = "abc" },
                ActiveVersionId = versionId,
                EventSubscriptions = [],
            }
        );
        await db.SaveChangesAsync();
    }
}
