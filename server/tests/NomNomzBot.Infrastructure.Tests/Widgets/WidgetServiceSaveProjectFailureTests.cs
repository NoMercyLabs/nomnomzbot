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
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
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
/// A widget project save the build rejects returns every problem with its file, line and column in the failure's
/// error data (what the API writes as <c>data.errors</c>), so the editor can mark the line. The error code and the
/// message stay what they were. Runs the real esbuild and the real Vue compiler, over a real SQLite database.
/// </summary>
public sealed class WidgetServiceSaveProjectFailureTests : IClassFixture<VueSfcCompilerFixture>
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly VueSfcCompilerFixture _fixture;

    public WidgetServiceSaveProjectFailureTests(VueSfcCompilerFixture fixture) =>
        _fixture = fixture;

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
        WidgetSqliteTestDatabase database,
        string framework
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
                Framework = framework,
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, widgetId);
    }

    private static async Task<(Result<WidgetVersionDetail> Saved, int Versions)> SaveAsync(
        WidgetServiceSaveProjectFailureTests self,
        string framework,
        Dictionary<string, string> files,
        string entry,
        params string[] dependencies
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database, framework);
        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetVersionDetail> saved = await self.NewService(db)
            .SaveProjectAsync(
                channel.ToString(),
                widget.ToString(),
                new(files, new(entry, "widget", framework, dependencies))
            );
        return (saved, await db.WidgetVersions.CountAsync(v => v.WidgetId == widget));
    }

    private static JsonElement ErrorsOf(Result<WidgetVersionDetail> failed)
    {
        failed
            .ErrorData.Should()
            .NotBeNull("a rejected save carries every problem, not only a message");
        failed.ErrorData.Should().BeAssignableTo<IResponseErrorData>();
        string json = JsonSerializer.Serialize(
            failed.ErrorData,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }
        );
        return JsonDocument.Parse(json).RootElement.GetProperty("errors");
    }

    [Theory]
    [InlineData("react")]
    [InlineData("svelte")]
    public async Task SaveProject_UnsupportedFramework_FailsWithTheFrameworkCodeAndStoresNothing(
        string framework
    )
    {
        (Result<WidgetVersionDetail> saved, int versions) = await SaveAsync(
            this,
            framework,
            new() { ["index.tsx"] = "export default () => null;" },
            "index.tsx"
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("WIDGET_FRAMEWORK_UNSUPPORTED");
        JsonElement errors = ErrorsOf(saved);
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("code").GetString().Should().Be("WIDGET_FRAMEWORK_UNSUPPORTED");
        versions.Should().Be(0, "a rejected save stores no version");
    }

    [Fact]
    public async Task SaveProject_VueTsSyntaxErrorInAnImportedFile_CarriesItsFileLineAndColumn()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import { v } from './lib/util';\ndocument.title = v;\n",
            ["lib/util.ts"] = "export const a = 1;\nexport const v = ;\n",
        };

        (Result<WidgetVersionDetail> saved, int versions) = await SaveAsync(
            this,
            "vue",
            files,
            "index.ts"
        );

        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        saved.ErrorMessage.Should().Contain("lib/util.ts:2:");
        JsonElement errors = ErrorsOf(saved);
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("file").GetString().Should().Be("lib/util.ts");
        errors[0].GetProperty("line").GetInt32().Should().Be(2);
        errors[0].GetProperty("column").GetInt32().Should().Be(18);
        errors[0].GetProperty("code").GetString().Should().Be("WIDGET_BUILD_FAILED");
        errors[0].GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        versions.Should().Be(0, "a rejected save stores no version");
    }

    [Fact]
    public async Task SaveProject_VueTsSyntaxErrorsInTwoFiles_CarriesBothProblems()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import './a';\nimport './b';\n",
            ["a.ts"] = "let x = 1;\nlet y = ;\n",
            ["b.ts"] = "let q = (;\n",
        };

        (Result<WidgetVersionDetail> saved, _) = await SaveAsync(this, "vue", files, "index.ts");

        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        JsonElement errors = ErrorsOf(saved);
        errors
            .EnumerateArray()
            .Select(e =>
                (
                    e.GetProperty("file").GetString(),
                    e.GetProperty("line").GetInt32(),
                    e.GetProperty("column").GetInt32() > 0
                )
            )
            .Should()
            .BeEquivalentTo([("a.ts", 2, true), ("b.ts", 1, true)]);
    }

    [Fact]
    public async Task SaveProject_BrokenVueSfc_CarriesTheSfcPathAndLine()
    {
        Dictionary<string, string> files = new()
        {
            ["App.vue"] =
                "<script setup lang=\"ts\">\nimport { ref } from 'vue'\nconst n = ref<number>(\n</script>\n<template><p>{{ n }}</p></template>\n",
        };

        (Result<WidgetVersionDetail> saved, int versions) = await SaveAsync(
            this,
            "vue",
            files,
            "App.vue"
        );

        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        JsonElement errors = ErrorsOf(saved);
        errors.GetArrayLength().Should().BeGreaterThan(0);
        errors[0].GetProperty("file").GetString().Should().Be("App.vue");
        errors[0].GetProperty("line").GetInt32().Should().BeGreaterThan(0);
        errors[0].GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        versions.Should().Be(0);
    }

    [Fact]
    public async Task SaveProject_SettingsJsonThatIsNotJson_CarriesTheFileAndTheJsonPosition()
    {
        Dictionary<string, string> files = new()
        {
            ["index.html"] = "<div></div>",
            ["settings.json"] = "{ \"fields\": [\n  { \"key\": \"a\", }\n] }",
        };

        (Result<WidgetVersionDetail> saved, int versions) = await SaveAsync(
            this,
            "vanilla",
            files,
            "index.html"
        );

        saved.ErrorCode.Should().Be("WIDGET_SETTINGS_INVALID");
        JsonElement errors = ErrorsOf(saved);
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("file").GetString().Should().Be("settings.json");
        errors[0].GetProperty("line").GetInt32().Should().Be(2);
        errors[0].GetProperty("column").GetInt32().Should().BeGreaterThan(0);
        errors[0].GetProperty("code").GetString().Should().Be("WIDGET_SETTINGS_INVALID");
        versions.Should().Be(0);
    }

    [Fact]
    public async Task SaveProject_SettingsJsonWithABadField_CarriesTheFileWithoutAPosition()
    {
        Dictionary<string, string> files = new()
        {
            ["index.html"] = "<div></div>",
            ["settings.json"] =
                "{ \"fields\": [ { \"key\": \"a\", \"label\": \"A\", \"type\": \"slider\", \"default\": 1 } ] }",
        };

        (Result<WidgetVersionDetail> saved, _) = await SaveAsync(
            this,
            "vanilla",
            files,
            "index.html"
        );

        saved.ErrorCode.Should().Be("WIDGET_SETTINGS_INVALID");
        JsonElement errors = ErrorsOf(saved);
        errors.GetArrayLength().Should().Be(1);
        errors[0].GetProperty("file").GetString().Should().Be("settings.json");
        errors[0].TryGetProperty("line", out _).Should().BeFalse("a bad field has no single line");
        errors[0].GetProperty("message").GetString().Should().Contain("\"a\"");
    }

    [Fact]
    public async Task SaveProject_ProjectGuards_CarryTheirCodeAndNamePathOnlyWhenKnown()
    {
        (Result<WidgetVersionDetail> noEntry, _) = await SaveAsync(
            this,
            "vanilla",
            new() { ["other.html"] = "<div></div>" },
            "index.html"
        );
        (Result<WidgetVersionDetail> badPath, _) = await SaveAsync(
            this,
            "vanilla",
            new() { ["index.html"] = "<div></div>", ["../evil.js"] = "x" },
            "index.html"
        );
        (Result<WidgetVersionDetail> badDependency, _) = await SaveAsync(
            this,
            "vue",
            new() { ["index.ts"] = "let a = 1;" },
            "index.ts",
            "left-pad"
        );

        JsonElement entryErrors = ErrorsOf(noEntry);
        entryErrors[0].GetProperty("code").GetString().Should().Be("WIDGET_PROJECT_ENTRY_MISSING");
        entryErrors[0].TryGetProperty("line", out _).Should().BeFalse();
        JsonElement pathErrors = ErrorsOf(badPath);
        pathErrors[0].GetProperty("code").GetString().Should().Be("WIDGET_PROJECT_PATH_INVALID");
        pathErrors[0].GetProperty("file").GetString().Should().Be("../evil.js");
        pathErrors[0].TryGetProperty("line", out _).Should().BeFalse();
        JsonElement dependencyErrors = ErrorsOf(badDependency);
        dependencyErrors[0]
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("WIDGET_DEPENDENCY_NOT_ALLOWED");
        dependencyErrors[0].TryGetProperty("file", out _).Should().BeFalse();
        noEntry.ErrorCode.Should().Be("VALIDATION_FAILED");
        badPath.ErrorCode.Should().Be("VALIDATION_FAILED");
        badDependency.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
