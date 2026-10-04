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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The <c>default</c> of each field in a widget's settings.json must reach the page even when the streamer never
/// saved the settings form: the generated settings type says every setting always has a value. A saved value wins
/// over the default. The overlay manifest, the join (<c>GetEffectiveSettingsAsync</c>) and the live settings push
/// all hand the page the same merged bag.
/// </summary>
public sealed class WidgetServiceSettingsDefaultsTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();
    private static readonly Guid WidgetId = Guid.CreateVersion7();

    private const string Declaration = """
        { "fields": [
          { "key": "accent", "type": "color", "default": "#ff00aa" },
          { "key": "size", "type": "number", "default": 12 },
          { "key": "loud", "type": "bool", "default": true }
        ] }
        """;

    [Fact]
    public async Task The_manifest_entry_carries_the_declared_defaults_when_nothing_was_saved()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, saved: new(), declaration: Declaration);
        await using WidgetTestDbContext db = database.NewContext();

        Result<OverlayManifest> result = await NewService(db, Substitute.For<IEventBus>())
            .GetOverlayManifestAsync("tok");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Dictionary<string, object?> settings = result.Value.Widgets.Single().Settings;
        settings["accent"].Should().Be("#ff00aa");
        settings["size"].Should().Be(12d);
        settings["loud"].Should().Be(true);
    }

    [Fact]
    public async Task A_saved_value_wins_over_the_declared_default_in_the_manifest()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(
            database,
            saved: new() { ["accent"] = "#000000" },
            declaration: Declaration
        );
        await using WidgetTestDbContext db = database.NewContext();

        Result<OverlayManifest> result = await NewService(db, Substitute.For<IEventBus>())
            .GetOverlayManifestAsync("tok");

        Dictionary<string, object?> settings = result.Value.Widgets.Single().Settings;
        settings["accent"].Should().Be("#000000");
        settings["size"].Should().Be(12d);
    }

    [Fact]
    public async Task The_join_settings_carry_the_declared_defaults_and_let_a_saved_value_win()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, saved: new() { ["size"] = 40d }, declaration: Declaration);
        await using WidgetTestDbContext db = database.NewContext();

        Result<Dictionary<string, object>> result = await NewService(
                db,
                Substitute.For<IEventBus>()
            )
            .GetEffectiveSettingsAsync(ChannelId, WidgetId);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value["accent"].Should().Be("#ff00aa");
        result.Value["size"].Should().Be(40d);
        result.Value["loud"].Should().Be(true);
    }

    [Fact]
    public async Task A_widget_without_a_declaration_keeps_its_saved_bag_unchanged()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, saved: new() { ["rewardId"] = "abc" }, declaration: null);
        await using WidgetTestDbContext db = database.NewContext();

        Result<Dictionary<string, object>> result = await NewService(
                db,
                Substitute.For<IEventBus>()
            )
            .GetEffectiveSettingsAsync(ChannelId, WidgetId);

        result
            .Value.Should()
            .BeEquivalentTo(new Dictionary<string, object> { ["rewardId"] = "abc" });
    }

    [Fact]
    public async Task A_settings_save_announces_the_declared_defaults_for_unsaved_fields()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database, saved: new(), declaration: Declaration);
        await using WidgetTestDbContext db = database.NewContext();
        IEventBus bus = Substitute.For<IEventBus>();

        Result<WidgetDetail> result = await NewService(db, bus)
            .UpdateAsync(
                ChannelId.ToString(),
                WidgetId.ToString(),
                new() { Settings = new() { ["size"] = 40d } }
            );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetSettingsChangedEvent>(e =>
                    (double)e.Settings["size"] == 40d
                    && (string)e.Settings["accent"] == "#ff00aa"
                    && (bool)e.Settings["loud"]
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static WidgetService NewService(WidgetTestDbContext db, IEventBus bus) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            bus,
            Substitute.For<IWidgetBuildService>(),
            new WidgetSettingsSchemaProvider(),
            new FakeTimeProvider(),
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    private static async Task SeedAsync(
        WidgetSqliteTestDatabase database,
        Dictionary<string, object> saved,
        string? declaration
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "tok",
            }
        );

        Dictionary<string, string> files = new() { ["index.ts"] = "export {}" };
        if (declaration is not null)
            files["settings.json"] = declaration;
        Guid versionId = Guid.CreateVersion7();
        db.WidgetVersions.Add(
            new()
            {
                Id = versionId,
                WidgetId = WidgetId,
                BroadcasterId = ChannelId,
                VersionNumber = 1,
                FilesJson = ProjectJson.SerializeFiles(files),
                BuildStatus = "success",
                ContentHash = "hash123",
            }
        );
        db.Widgets.Add(
            new()
            {
                Id = WidgetId,
                BroadcasterId = ChannelId,
                Name = "Defaults",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
                Settings = saved,
                ActiveVersionId = versionId,
                EventSubscriptions = [],
            }
        );
        await db.SaveChangesAsync();
    }
}
