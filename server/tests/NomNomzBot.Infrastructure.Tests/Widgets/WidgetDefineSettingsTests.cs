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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A self-authored widget declares its settings form from its own code with <c>nnz.defineSettings({...})</c>: the
/// build reads the object literal from the source (the server never runs overlay code), the schema read serves
/// those fields, a literal that is not static fails the build with its line, and settings.json keeps working.
/// </summary>
public sealed class WidgetDefineSettingsTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string Declared = """
        <div id="w"></div>
        <script>
          const settings = nnz.defineSettings({
            // The form the dashboard shows.
            fields: [
              { key: 'fontSize', label: 'Font size', type: 'number', default: 24, },
              { key: "accent", label: 'Streamer\'s accent', type: "color", default: '#ff0066' },
              { key: 'mode', label: 'Mode', type: 'select', default: 'dark',
                options: [ { value: 'dark', label: 'Dark' }, { value: 'light', label: 'Light' } ] },
            ],
          });
        </script>
        """;

    private const string SettingsJson = """
        { "fields": [ { "key": "title", "label": "Title", "type": "text", "default": "Hello" } ] }
        """;

    private static readonly FakeTimeProvider Clock = new(new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly VueSfcCompilerFixture _fixture;

    public WidgetDefineSettingsTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private EsbuildWidgetBuildService NewBuildService() =>
        new(
            new ProcessRunner(),
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    private WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            NewBuildService(),
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IPlaybackStateReader>()
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

    private static ProjectDto Project(string html, string? settingsJson = null)
    {
        Dictionary<string, string> files = new() { ["index.html"] = html };
        if (settingsJson is not null)
            files["settings.json"] = settingsJson;
        return new(files, new("index.html", "widget", "vanilla", []));
    }

    [Fact]
    public async Task GetSettingsSchema_SourceCallsDefineSettings_ReturnsExactlyThoseFields()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (await service.SaveProjectAsync(channel.ToString(), widget.ToString(), Project(Declared)))
            .IsSuccess.Should()
            .BeTrue();

        Result<WidgetSettingsSchema> schema = await service.GetSettingsSchemaAsync(
            channel.ToString(),
            widget.ToString()
        );

        schema.IsSuccess.Should().BeTrue(schema.ErrorMessage);
        schema.Value.WidgetKey.Should().Be("custom");
        schema
            .Value.Fields.Select(f => (f.Key, f.Type, f.Default, f.Label.Text))
            .Should()
            .Equal(
                ("fontSize", "number", 24d, "Font size"),
                ("accent", "color", "#ff0066", "Streamer's accent"),
                ("mode", "select", "dark", "Mode")
            );
        schema.Value.Fields[2].Options!.Select(o => o.Value).Should().Equal("dark", "light");
    }

    [Fact]
    public async Task GetEffectiveSettings_DefineSettingsDefaults_FillUnsavedKeys()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (await service.SaveProjectAsync(channel.ToString(), widget.ToString(), Project(Declared)))
            .IsSuccess.Should()
            .BeTrue();

        Result<Dictionary<string, object>> effective = await service.GetEffectiveSettingsAsync(
            channel,
            widget
        );

        effective.IsSuccess.Should().BeTrue(effective.ErrorMessage);
        effective.Value["accent"].Should().Be("#ff0066");
        effective.Value["mode"].Should().Be("dark");
    }

    [Fact]
    public async Task Build_DefineSettingsWithNonLiteralValue_FailsNamingTheLine()
    {
        const string html =
            "<script>\n  const size = 24;\n  nnz.defineSettings({\n    fields: [\n      { key: 'a', label: 'A', type: 'number', default: size },\n    ],\n  });\n</script>";
        ProjectDto project = Project(html);

        Result<WidgetBuildOutput> build = await NewBuildService()
            .BuildAsync(new(project.Manifest.ToManifest(), project.Files));

        build.IsFailure.Should().BeTrue();
        build.ErrorCode.Should().Be(CustomWidgetSettingsDeclaration.InvalidCode);
        ProjectBuildError error = ((ProjectBuildFailure)build.ErrorData!).Errors.Single();
        error.File.Should().Be("index.html");
        error.Line.Should().Be(5);
        error.Message.Should().Contain("size").And.Contain("literal");
    }

    [Fact]
    public async Task Build_DefineSettingsWithVariableArgument_FailsNamingTheLine()
    {
        ProjectDto project = Project(
            "<script>\nconst form = {};\nnnz.defineSettings(form);\n</script>"
        );

        Result<WidgetBuildOutput> build = await NewBuildService()
            .BuildAsync(new(project.Manifest.ToManifest(), project.Files));

        build.IsFailure.Should().BeTrue();
        ((ProjectBuildFailure)build.ErrorData!).Errors.Single().Line.Should().Be(3);
    }

    [Fact]
    public async Task Build_DefineSettingsMentionedInAComment_ReadsTheRealCall()
    {
        const string html =
            "<script>\n// call nnz.defineSettings(form) once\nnnz.defineSettings({ fields: [ { key: 'a', label: 'A', type: 'bool', default: true } ] });\n</script>";
        ProjectDto project = Project(html);

        Result<WidgetBuildOutput> build = await NewBuildService()
            .BuildAsync(new(project.Manifest.ToManifest(), project.Files));

        build.IsSuccess.Should().BeTrue(build.ErrorMessage);
        CustomWidgetSettingsDeclaration
            .FromProject(project.Files)!
            .Value.Select(f => f.Key)
            .Should()
            .Equal("a");
    }

    [Fact]
    public void FromProject_NegativeNumberAndPlainTemplate_ReadAsLiterals()
    {
        ProjectDto project = Project(
            "<script>\nnnz.defineSettings({ fields: [\n{ key: 'a', label: `A`, type: 'number', default: -5 } ] });\n</script>"
        );

        IReadOnlyList<WidgetSettingsField> fields = CustomWidgetSettingsDeclaration
            .FromProject(project.Files)!
            .Value;

        fields.Select(f => (f.Key, f.Default, f.Label.Text)).Should().Equal(("a", -5d, "A"));
    }

    [Fact]
    public async Task Build_DefineSettingsWithTemplateExpression_FailsNamingTheLine()
    {
        ProjectDto project = Project(
            "<script>\nconst x = 'a';\nnnz.defineSettings({ fields: [\n{ key: `${x}`, label: 'A', type: 'bool', default: true } ] });\n</script>"
        );

        Result<WidgetBuildOutput> build = await NewBuildService()
            .BuildAsync(new(project.Manifest.ToManifest(), project.Files));

        build.IsFailure.Should().BeTrue();
        ((ProjectBuildFailure)build.ErrorData!).Errors.Single().Line.Should().Be(4);
    }

    [Fact]
    public async Task Build_SettingsJsonAndDefineSettingsTogether_FailsNamingBoth()
    {
        ProjectDto project = Project(Declared, SettingsJson);

        Result<WidgetBuildOutput> build = await NewBuildService()
            .BuildAsync(new(project.Manifest.ToManifest(), project.Files));

        build.IsFailure.Should().BeTrue();
        build.ErrorCode.Should().Be(CustomWidgetSettingsDeclaration.InvalidCode);
        build.ErrorMessage.Should().Contain("settings.json").And.Contain("defineSettings");
    }

    [Fact]
    public async Task GetSettingsSchema_SettingsJsonOnly_StillReturnsItsFields()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (
            await service.SaveProjectAsync(
                channel.ToString(),
                widget.ToString(),
                Project("<div></div>", SettingsJson)
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        Result<WidgetSettingsSchema> schema = await service.GetSettingsSchemaAsync(
            channel.ToString(),
            widget.ToString()
        );

        schema.IsSuccess.Should().BeTrue(schema.ErrorMessage);
        schema.Value.Fields.Select(f => f.Key).Should().Equal("title");
    }

    [Fact]
    public async Task SaveProject_NonLiteralDefineSettings_StoresNoVersion()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);

        Result<WidgetVersionDetail> saved = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project("<script>nnz.defineSettings(load());</script>")
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be(CustomWidgetSettingsDeclaration.InvalidCode);
        db.WidgetVersions.Count(v => v.WidgetId == widget).Should().Be(0);
    }
}
