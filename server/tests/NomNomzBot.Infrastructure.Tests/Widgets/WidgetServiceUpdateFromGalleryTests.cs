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
/// An installed first-party/community widget is a snapshot: <see cref="FirstPartyWidgetCatalogueSeeder"/> can move
/// the gallery item's <see cref="WidgetGalleryItem.SourceCode"/> forward (bumping
/// <see cref="WidgetGalleryItem.SourceRevision"/>), but nothing was ever pulling that forward into a channel's
/// already-installed <see cref="Widget"/> — an install from June silently kept running June's code forever. These
/// tests prove the fix: <see cref="WidgetDetail.GalleryUpdateAvailable"/> flips on when the gallery moves ahead of
/// what the widget was built from, and <see cref="IWidgetService.UpdateFromGalleryAsync"/> is the explicit,
/// streamer-initiated action that pulls the update in as a new compiled version (never a silent platform rebuild).
/// </summary>
public sealed class WidgetServiceUpdateFromGalleryTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            NewBuildService(),
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db)
        );

    private static IWidgetBuildService NewBuildService()
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("BUNDLE", "hash", "")));
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
                OverlayToken = "tok",
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Guid> SeedGalleryItemAsync(
        WidgetSqliteTestDatabase database,
        string source,
        int sourceRevision = 1
    )
    {
        Guid id = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new()
            {
                Id = id,
                Name = "Alerts",
                Framework = "vue",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = "alerts",
                SourceCode = source,
                SourceRevision = sourceRevision,
                ReviewStatus = "verified",
                AvailableInSaaS = true,
                DefaultEventSubscriptions = [],
                DefaultSettings = new(),
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task A_fresh_install_carries_no_update_available()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database, "SOURCE_V1");

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.GalleryUpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Once_the_gallery_item_moves_to_a_newer_revision_the_installed_widget_flags_it()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database, "SOURCE_V1");

        Guid widgetId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> installed = await NewService(db)
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
            installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
            widgetId = installed.Value.Id;
        }

        // The catalogue seeder ships a new revision of the source (a real re-seed, or simulated here directly).
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.SourceCode = "SOURCE_V2";
            item.SourceRevision = 2;
            await db.SaveChangesAsync();
        }

        await using WidgetTestDbContext read = database.NewContext();
        Result<WidgetDetail> detail = await NewService(read)
            .GetAsync(channel.ToString(), widgetId.ToString());

        detail.IsSuccess.Should().BeTrue(detail.ErrorMessage);
        detail.Value.GalleryUpdateAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Applying_the_update_compiles_the_new_source_and_clears_the_flag()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database, "SOURCE_V1");

        Guid widgetId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> installed = await NewService(db)
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
            widgetId = installed.Value.Id;
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.SourceCode = "SOURCE_V2";
            item.SourceRevision = 2;
            await db.SaveChangesAsync();
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> updated = await NewService(db)
                .UpdateFromGalleryAsync(channel.ToString(), widgetId.ToString());
            updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
            updated.Value.GalleryUpdateAvailable.Should().BeFalse();
        }

        await using WidgetTestDbContext read = database.NewContext();
        Widget widget = await read.Widgets.SingleAsync(w => w.Id == widgetId);
        widget.InstalledSourceRevision.Should().Be(2);

        WidgetVersion active = await read.WidgetVersions.SingleAsync(v =>
            v.Id == widget.ActiveVersionId
        );
        active.SourceCode.Should().Be("SOURCE_V2");
        active.VersionNumber.Should().Be(2); // v1 from install, v2 from the applied update — never an edit in place
    }

    [Fact]
    public async Task A_widget_never_linked_to_the_gallery_refuses_the_update_honestly()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);

        Guid widgetId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> created = await NewService(db)
                .CreateAsync(
                    channel.ToString(),
                    new CreateWidgetRequest { Name = "Custom", Framework = "vanilla" }
                );
            widgetId = created.Value.Id;
        }

        await using WidgetTestDbContext readDb = database.NewContext();
        Result<WidgetDetail> result = await NewService(readDb)
            .UpdateFromGalleryAsync(channel.ToString(), widgetId.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_NOT_GALLERY_LINKED");
    }
}
