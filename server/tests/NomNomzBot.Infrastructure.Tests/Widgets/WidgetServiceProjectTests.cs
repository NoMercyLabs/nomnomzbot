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
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior tests for the widget multi-file project CRUD (dev-platform.md §8). Each proves a consequence: the
/// persisted version's stored file set + manifest + compiled bundle, the round-trip through GET, and — the point of
/// the whole slice — that esbuild resolves a cross-file <c>lib/</c> import so the imported module's code lands IN the
/// saved bundle. Runs the REAL <see cref="EsbuildWidgetBuildService"/> (the trust boundary) over real SQLite, so the
/// append-only unique <c>(WidgetId, VersionNumber)</c> constraint and the actual build guards are exercised.
/// </summary>
public sealed class WidgetServiceProjectTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string CrossFileMarker = "NNZ_PROJECT_CROSSFILE_MARKER";

    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)
    );

    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private readonly VueSfcCompilerFixture _fixture;

    public WidgetServiceProjectTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    // The production build service, wired to the real esbuild binary (path from Widgets:EsbuildPath / PATH). Vanilla
    // and every pre-bundle guard run WITHOUT the binary; only cross-file bundling needs it, so tests that do gate on it.
    private EsbuildWidgetBuildService RealBuild() =>
        new(
            new ProcessRunner(),
            _fixture.Compiler,
            new WidgetDependencyAllowlist(),
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    private WidgetService NewService(WidgetTestDbContext db, IEventBus bus) =>
        new(db, EmptyConfig, bus, RealBuild(), new WidgetSettingsSchemaProvider(), Clock);

    private static async Task<Guid> SeedChannelAsync(WidgetSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = channelId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "tok",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Guid> SeedWidgetAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        string framework
    )
    {
        Guid widgetId = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.Widgets.Add(
            new Widget
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "Alerts",
                Framework = framework,
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return widgetId;
    }

    private static ProjectDto Project(
        string framework,
        string entry,
        Dictionary<string, string> files,
        params string[] dependencies
    ) => new(files, new ProjectManifestDto(entry, "widget", framework, dependencies));

    [Fact]
    public async Task SaveProject_PersistsVersion_WithStoredProject_AndPointsActive()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "vanilla");
        IEventBus bus = Substitute.For<IEventBus>();

        // A vanilla project needs no bundler binary — the entry file IS the bundle — so this proves the persistence
        // shape deterministically on any host, including binary-less CI.
        const string html = "<div id=\"w\">hi</div>";
        Dictionary<string, string> files = new()
        {
            ["index.html"] = html,
            ["lib/theme.css"] = ".w{color:red}",
        };

        WidgetVersionDetail detail;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus);
            Result<WidgetVersionDetail> result = await service.SaveProjectAsync(
                channel.ToString(),
                widget.ToString(),
                Project("vanilla", "index.html", files)
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            detail = result.Value;
        }

        detail.VersionNumber.Should().Be(1);
        detail.BuildStatus.Should().Be("success");

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion stored = await db.WidgetVersions.SingleAsync(v => v.WidgetId == widget);
            stored.Id.Should().Be(detail.Id);
            stored.BuildStatus.Should().Be("success");
            stored.SourceCode.Should().Be(html); // legacy column keeps the entry's content
            stored.CompiledBundle.Should().Be(html); // vanilla passes through
            stored.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

            // The WHOLE file set + manifest are stored, not just the entry.
            Dictionary<string, string>? storedFiles = ProjectJson.DeserializeFiles(
                stored.FilesJson
            );
            storedFiles.Should().BeEquivalentTo(files);
            ProjectManifest? storedManifest = ProjectJson.DeserializeManifest(stored.ManifestJson);
            storedManifest!.Entry.Should().Be("index.html");
            storedManifest.Kind.Should().Be("widget");
            storedManifest.Framework.Should().Be("vanilla");

            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().Be(stored.Id);
        }

        // The cache-bust event fired once for the new live version.
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetBuildSucceededEvent>(e =>
                    e.WidgetId == widget && e.VersionNumber == 1
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetProject_RoundTripsTheSavedFilesAndManifest()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "vanilla");
        Dictionary<string, string> files = new()
        {
            ["index.html"] = "<div>panel</div>",
            ["lib/util.js"] = "export const n = 1;",
        };

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, Substitute.For<IEventBus>());
            (
                await service.SaveProjectAsync(
                    channel.ToString(),
                    widget.ToString(),
                    Project("vanilla", "index.html", files)
                )
            )
                .IsSuccess.Should()
                .BeTrue();
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, Substitute.For<IEventBus>());
            Result<ProjectDto> got = await service.GetProjectAsync(
                channel.ToString(),
                widget.ToString()
            );

            got.IsSuccess.Should().BeTrue(got.ErrorMessage);
            got.Value.Files.Should().BeEquivalentTo(files); // exact file set round-trips
            got.Value.Manifest.Entry.Should().Be("index.html");
            got.Value.Manifest.Kind.Should().Be("widget");
            got.Value.Manifest.Framework.Should().Be("vanilla");
            got.Value.Manifest.Dependencies.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SaveProject_ReactCrossFileImport_BundlesTheImportedModuleIntoTheStoredBundle()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "react");
        Dictionary<string, string> files = new()
        {
            ["index.tsx"] =
                "import { GREETING } from './lib/util';\n"
                + "const el = document.createElement('div');\n"
                + "el.textContent = GREETING;\n"
                + "document.body.appendChild(el);\n",
            ["lib/util.ts"] = $"export const GREETING: string = '{CrossFileMarker}';\n",
        };

        Result<WidgetVersionDetail> result;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, Substitute.For<IEventBus>());
            result = await service.SaveProjectAsync(
                channel.ToString(),
                widget.ToString(),
                Project("react", "index.tsx", files)
            );
        }

        if (result.IsFailure)
        {
            // esbuild absent (binary-less CI): the save failed cleanly as unavailable and persisted NO version — the
            // bundling assertion needs the binary, but the "nothing persisted on failure" invariant still holds.
            result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE", result.ErrorMessage);
            await using WidgetTestDbContext db = database.NewContext();
            (await db.WidgetVersions.AnyAsync(v => v.WidgetId == widget)).Should().BeFalse();
            return;
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion stored = await db.WidgetVersions.SingleAsync(v => v.WidgetId == widget);
            stored.BuildStatus.Should().Be("success");
            // The imported lib module's code is IN the one saved bundle — cross-file resolution proven end-to-end.
            stored.CompiledBundle.Should().Contain(CrossFileMarker);
            stored.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().Be(stored.Id);
            storedWidget.Framework.Should().Be("react"); // manifest framework synced onto the widget
        }
    }

    [Fact]
    public async Task SaveProject_ManifestEntryMissingFromFiles_FailsValidation_PersistsNoVersion()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "react");
        Dictionary<string, string> files = new() { ["index.tsx"] = "export default 1;" };

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>());
        Result<WidgetVersionDetail> result = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project("react", "main.tsx", files) // entry not present
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("main.tsx");
        (await db.WidgetVersions.AnyAsync(v => v.WidgetId == widget)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveProject_NonAllowlistedDependency_FailsValidation_PersistsNoVersion()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "react");
        Dictionary<string, string> files = new()
        {
            ["index.tsx"] = "import _ from 'lodash';\nexport default () => _.identity(1);\n",
        };

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>());
        Result<WidgetVersionDetail> result = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project("react", "index.tsx", files, "lodash")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("lodash");
        (await db.WidgetVersions.AnyAsync(v => v.WidgetId == widget)).Should().BeFalse();
    }

    [Fact]
    public async Task Project_ReadAndWrite_AreScopedToTheOwningTenant()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "vanilla");
        Guid otherChannel = await SeedChannelAsync(database);
        Dictionary<string, string> files = new() { ["index.html"] = "<div>x</div>" };

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>());

        // A caller acting on a channel they do not own cannot write the widget's project…
        Result<WidgetVersionDetail> write = await service.SaveProjectAsync(
            otherChannel.ToString(),
            widget.ToString(),
            Project("vanilla", "index.html", files)
        );
        write.IsFailure.Should().BeTrue();
        write.ErrorCode.Should().Be("NOT_FOUND");
        (await db.WidgetVersions.AnyAsync(v => v.WidgetId == widget)).Should().BeFalse();

        // …nor read it.
        Result<ProjectDto> read = await service.GetProjectAsync(
            otherChannel.ToString(),
            widget.ToString()
        );
        read.IsFailure.Should().BeTrue();
        read.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetProject_ForAWidgetWithNoVersion_IsNotFound()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel, "vanilla");

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>());
        Result<ProjectDto> got = await service.GetProjectAsync(
            channel.ToString(),
            widget.ToString()
        );

        got.IsFailure.Should().BeTrue();
        got.ErrorCode.Should().Be("NOT_FOUND");
    }
}
