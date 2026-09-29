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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A channel edits its own copy of a system (first-party) overlay widget in the regular widget editor. These tests
/// follow that flow through the real service over real SQLite: the editor opens the catalogue source, a save
/// appends a channel-owned version the overlay then serves, "Reset to system default" brings the catalogue source
/// back as a NEW version (the edit stays in the history), and a catalogue re-seed never touches the channel's edit.
/// The build stand-in derives the bundle from the entry file, so every assertion on the served bundle proves which
/// source is actually live.
/// </summary>
public sealed class WidgetServiceSystemWidgetEditTests
{
    private const string CatalogueSource = "<template><div>catalogue</div></template>";
    private const string EditedSource = "<template><div>my own alerts</div></template>";

    private static readonly FakeTimeProvider Clock = new(new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(
        WidgetTestDbContext db,
        IWidgetBuildService? build = null
    ) =>
        new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            build ?? SourceEchoingBuild(),
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    // The bundle is "compiled::" + the entry file's source, so the served bundle names the source that is live.
    private static IWidgetBuildService SourceEchoingBuild()
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                WidgetBuildInput input = call.Arg<WidgetBuildInput>();
                string entrySource = input.Files[input.Manifest.Entry];
                return Result.Success(
                    new WidgetBuildOutput("compiled::" + entrySource, new string('a', 64), "")
                );
            });
        return build;
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
                TwitchChannelId = "12345",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = "channel-token",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    // The catalogue row the seeder would write for the "alerts" system surface.
    private static async Task SeedAlertsCatalogueItemAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new()
            {
                Name = "Alerts",
                Framework = "vue",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = "alerts",
                SourceCode = CatalogueSource,
                SourceRevision = 1,
                ReviewStatus = "verified",
                AvailableInSaaS = true,
                DefaultEventSubscriptions = ["follow"],
                DefaultSettings = new() { ["durationMs"] = 6000 },
            }
        );
        await db.SaveChangesAsync();
    }

    // Provision the system surface exactly as onboarding does, then open it in the editor and save an edit.
    private static async Task<Guid> ProvisionAndEditAsync(
        WidgetSqliteTestDatabase database,
        Guid channel
    )
    {
        Guid widgetId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> provisioned = await NewService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), "alerts");
            provisioned.IsSuccess.Should().BeTrue(provisioned.ErrorMessage);
            widgetId = provisioned.Value.Id;
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db);
            Result<ProjectDto> opened = await service.GetProjectAsync(
                channel.ToString(),
                widgetId.ToString()
            );
            opened.IsSuccess.Should().BeTrue(opened.ErrorMessage);

            Dictionary<string, string> editedFiles = new(opened.Value.Files)
            {
                [opened.Value.Manifest.Entry] = EditedSource,
            };
            Result<WidgetVersionDetail> saved = await service.SaveProjectAsync(
                channel.ToString(),
                widgetId.ToString(),
                opened.Value with
                {
                    Files = editedFiles,
                }
            );
            saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        }

        return widgetId;
    }

    private static async Task<string> ServedBundleAsync(
        WidgetSqliteTestDatabase database,
        Guid widgetId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        string widgetToken = await db
            .Widgets.Where(w => w.Id == widgetId)
            .Select(w => w.OverlayToken)
            .SingleAsync();
        Result<OverlayBundle> bundle = await NewService(db)
            .GetOverlayBundleAsync(widgetToken, widgetId.ToString());
        bundle.IsSuccess.Should().BeTrue(bundle.ErrorMessage);
        return bundle.Value.Content;
    }

    [Fact]
    public async Task The_editor_opens_a_system_widget_on_its_catalogue_source()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await SeedAlertsCatalogueItemAsync(database);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        Result<WidgetDetail> provisioned = await service.EnsureSystemWidgetAsync(
            channel.ToString(),
            "alerts"
        );
        Result<ProjectDto> opened = await service.GetProjectAsync(
            channel.ToString(),
            provisioned.Value.Id.ToString()
        );

        opened.IsSuccess.Should().BeTrue(opened.ErrorMessage);
        opened.Value.Manifest.Framework.Should().Be("vue");
        opened.Value.Files.Should().ContainKey(opened.Value.Manifest.Entry);
        opened.Value.Files[opened.Value.Manifest.Entry].Should().Be(CatalogueSource);
        provisioned.Value.Source.Should().Be("first_party");
        provisioned.Value.IsCustomized.Should().BeFalse();
    }

    [Fact]
    public async Task Saving_an_edit_makes_the_overlay_serve_the_channel_source_and_marks_it_customized()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await SeedAlertsCatalogueItemAsync(database);

        Guid widgetId = await ProvisionAndEditAsync(database, channel);

        (await ServedBundleAsync(database, widgetId)).Should().Be("compiled::" + EditedSource);

        await using WidgetTestDbContext read = database.NewContext();
        Widget widget = await read.Widgets.SingleAsync(w => w.Id == widgetId);
        widget.CatalogueVersionNumber.Should().Be(1); // the install compile carried the catalogue source
        List<WidgetVersion> versions = await read
            .WidgetVersions.Where(v => v.WidgetId == widgetId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        versions.Select(v => v.SourceCode).Should().Equal(CatalogueSource, EditedSource);
        widget.ActiveVersionId.Should().Be(versions[1].Id);

        // The list the dashboard renders marks it as a first-party widget with a channel edit.
        Result<PagedList<WidgetDetail>> listed = await NewService(read)
            .ListAsync(channel.ToString(), new PaginationParams());
        WidgetDetail row = listed.Value.Items.Single(w => w.Id == widgetId);
        row.Source.Should().Be("first_party");
        row.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_to_system_default_serves_the_catalogue_source_as_a_new_version_and_keeps_the_edit()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await SeedAlertsCatalogueItemAsync(database);
        Guid widgetId = await ProvisionAndEditAsync(database, channel);

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> reset = await NewService(db)
                .UpdateFromGalleryAsync(channel.ToString(), widgetId.ToString());
            reset.IsSuccess.Should().BeTrue(reset.ErrorMessage);
            reset.Value.IsCustomized.Should().BeFalse();
        }

        (await ServedBundleAsync(database, widgetId)).Should().Be("compiled::" + CatalogueSource);

        await using WidgetTestDbContext read = database.NewContext();
        List<WidgetVersion> versions = await read
            .WidgetVersions.Where(v => v.WidgetId == widgetId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        versions
            .Select(v => v.SourceCode)
            .Should()
            .Equal(CatalogueSource, EditedSource, CatalogueSource);
        Widget widget = await read.Widgets.SingleAsync(w => w.Id == widgetId);
        widget.CatalogueVersionNumber.Should().Be(3);
        widget.ActiveVersionId.Should().Be(versions[2].Id);
    }

    [Fact]
    public async Task A_reset_whose_build_fails_says_so_and_leaves_the_edit_live_and_marked()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await SeedAlertsCatalogueItemAsync(database);
        Guid widgetId = await ProvisionAndEditAsync(database, channel);

        IWidgetBuildService brokenBuild = Substitute.For<IWidgetBuildService>();
        brokenBuild
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<WidgetBuildOutput>(
                    "esbuild could not be started.",
                    "WIDGET_BUILD_TOOL_UNAVAILABLE"
                )
            );

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> reset = await NewService(db, brokenBuild)
                .UpdateFromGalleryAsync(channel.ToString(), widgetId.ToString());
            reset.IsFailure.Should().BeTrue();
            reset.ErrorCode.Should().Be("WIDGET_BUILD_FAILED");
            reset.ErrorMessage.Should().Contain("esbuild");
        }

        (await ServedBundleAsync(database, widgetId)).Should().Be("compiled::" + EditedSource);

        await using WidgetTestDbContext read = database.NewContext();
        Widget widget = await read.Widgets.SingleAsync(w => w.Id == widgetId);
        widget.CatalogueVersionNumber.Should().Be(1); // the failed version 3 is not the new baseline
        Result<WidgetDetail> detail = await NewService(read)
            .GetAsync(channel.ToString(), widgetId.ToString());
        detail.Value.IsCustomized.Should().BeTrue();
    }

    [Fact]
    public async Task A_catalogue_reseed_leaves_the_channel_edit_live()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await SeedAlertsCatalogueItemAsync(database);
        Guid widgetId = await ProvisionAndEditAsync(database, channel);

        // The real seeder ships the real embedded alerts.vue over the catalogue row — a catalogue update.
        await using (WidgetTestDbContext db = database.NewContext())
        {
            await new FirstPartyWidgetCatalogueSeeder(db).SeedAsync();
            await db.SaveChangesAsync();
        }

        await using (WidgetTestDbContext read = database.NewContext())
        {
            WidgetGalleryItem item = await read.WidgetGalleryItems.SingleAsync(i =>
                i.NaturalKey == "alerts"
            );
            item.SourceCode.Should().NotBe(CatalogueSource);
            item.SourceRevision.Should().Be(2);
            (await read.WidgetVersions.CountAsync(v => v.WidgetId == widgetId)).Should().Be(2);

            Result<WidgetDetail> detail = await NewService(read)
                .GetAsync(channel.ToString(), widgetId.ToString());
            detail.Value.IsCustomized.Should().BeTrue();
            detail.Value.GalleryUpdateAvailable.Should().BeTrue();
        }

        (await ServedBundleAsync(database, widgetId)).Should().Be("compiled::" + EditedSource);
    }
}
