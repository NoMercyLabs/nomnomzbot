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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A self-authored widget declares its settings in <c>settings.json</c>: the saved project's file feeds the
/// settings schema, an absent file keeps the no-schema answer, and an invalid file rejects the save before a
/// version is stored.
/// </summary>
public sealed class WidgetServiceCustomSettingsTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string Html = "<div id=\"w\">hi</div>";

    private const string Declaration = """
        { "fields": [
          { "key": "accent", "label": "Accent colour", "type": "color", "default": "#ff0066" },
          { "key": "title", "label": "Title", "type": "text", "default": "Hello" }
        ] }
        """;

    private static readonly FakeTimeProvider Clock = new(new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly VueSfcCompilerFixture _fixture;

    public WidgetServiceCustomSettingsTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            new EsbuildWidgetBuildService(
                new ProcessRunner(),
                _fixture.Compiler,
                new WidgetDependencyAllowlist(),
                new ConfigurationBuilder().AddEnvironmentVariables().Build(),
                NullLogger<EsbuildWidgetBuildService>.Instance
            ),
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    private static async Task<(Guid Channel, Guid Widget)> SeedAsync(
        WidgetSqliteTestDatabase database
    )
    {
        Guid channelId = Guid.CreateVersion7();
        Guid widgetId = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "tok",
            }
        );
        db.Widgets.Add(
            new()
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "My widget",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, widgetId);
    }

    private static ProjectDto Project(string? settingsJson)
    {
        Dictionary<string, string> files = new() { ["index.html"] = Html };
        if (settingsJson is not null)
            files["settings.json"] = settingsJson;
        return new(files, new("index.html", "widget", "vanilla", []));
    }

    [Fact]
    public async Task GetSettingsSchema_CustomWidgetWithSettingsJson_ReturnsItsDeclaredFields()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        Result<WidgetVersionDetail> saved = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project(Declaration)
        );
        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);

        Result<WidgetSettingsSchema> schema = await service.GetSettingsSchemaAsync(
            channel.ToString(),
            widget.ToString()
        );

        schema.IsSuccess.Should().BeTrue(schema.ErrorMessage);
        schema.Value.WidgetKey.Should().Be("custom");
        schema.Value.Name.Should().Be("My widget");
        schema
            .Value.Fields.Select(f => (f.Key, f.Type, f.Default, f.Label.Key))
            .Should()
            .Equal(
                ("accent", "color", "#ff0066", "Accent colour"),
                ("title", "text", "Hello", "Title")
            );
    }

    [Fact]
    public async Task GetSettingsSchema_CustomWidgetWithoutSettingsJson_KeepsNoSchemaCode()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (await service.SaveProjectAsync(channel.ToString(), widget.ToString(), Project(null)))
            .IsSuccess.Should()
            .BeTrue();

        Result<WidgetSettingsSchema> schema = await service.GetSettingsSchemaAsync(
            channel.ToString(),
            widget.ToString()
        );

        schema.IsFailure.Should().BeTrue();
        schema.ErrorCode.Should().Be("WIDGET_NO_SETTINGS_SCHEMA");
    }

    [Fact]
    public async Task SaveProject_InvalidSettingsJson_FailsAndStoresNoNewVersion()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (
            await service.SaveProjectAsync(
                channel.ToString(),
                widget.ToString(),
                Project(Declaration)
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        Result<WidgetVersionDetail> bad = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project(
                "{ \"fields\": [ { \"key\": \"a\", \"label\": \"A\", \"type\": \"slider\", \"default\": 1 } ] }"
            )
        );

        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("WIDGET_SETTINGS_INVALID");
        bad.ErrorMessage.Should().Contain("\"a\"");
        (await db.WidgetVersions.CountAsync(v => v.WidgetId == widget)).Should().Be(1);
    }
}
