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
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A widget whose build failed must never be reported as installed or cloned: the overlay would show nothing
/// while the dashboard says it worked, and the gallery install count would still go up.
/// </summary>
public sealed class WidgetServiceBuildFailureTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static IWidgetBuildService Build(Result<WidgetBuildOutput> result)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build.BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>()).Returns(result);
        return build;
    }

    private static WidgetService NewService(WidgetTestDbContext db, IWidgetBuildService build) =>
        new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            build,
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

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

    private static async Task<Guid> SeedGalleryItemAsync(WidgetSqliteTestDatabase database)
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
                SourceCode = "SOURCE_V1",
                SourceRevision = 1,
                ReviewStatus = "verified",
                AvailableInSaaS = true,
                DefaultEventSubscriptions = [],
                DefaultSettings = new(),
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    private static readonly Result<WidgetBuildOutput> Broken = Result.Failure<WidgetBuildOutput>(
        "esbuild could not be started.",
        "WIDGET_BUILD_TOOL_UNAVAILABLE"
    );

    private static readonly Result<WidgetBuildOutput> Fine = Result.Success(
        new WidgetBuildOutput("BUNDLE", "hash", "")
    );

    [Fact]
    public async Task Installing_a_widget_whose_build_failed_fails_and_leaves_the_install_count_alone()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database);

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db, Build(Broken))
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());

            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("WIDGET_BUILD_FAILED");
        }

        await using WidgetTestDbContext read = database.NewContext();
        (await read.WidgetGalleryItems.SingleAsync(i => i.Id == galleryItem))
            .InstallCount.Should()
            .Be(0);
    }

    [Fact]
    public async Task Installing_a_widget_whose_build_succeeds_still_counts_the_install()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database);

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db, Build(Fine))
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using WidgetTestDbContext read = database.NewContext();
        (await read.WidgetGalleryItems.SingleAsync(i => i.Id == galleryItem))
            .InstallCount.Should()
            .Be(1);
    }

    [Fact]
    public async Task Cloning_a_widget_whose_build_failed_fails_instead_of_returning_a_dead_clone()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid galleryItem = await SeedGalleryItemAsync(database);

        Guid installed;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> ok = await NewService(db, Build(Fine))
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
            installed = ok.Value.Id;
        }

        await using WidgetTestDbContext cloneDb = database.NewContext();
        Result<WidgetDetail> clone = await NewService(cloneDb, Build(Broken))
            .CloneToEditAsync(channel.ToString(), new() { InstalledWidgetId = installed });

        clone.IsFailure.Should().BeTrue();
        clone.ErrorCode.Should().Be("WIDGET_BUILD_FAILED");
    }
}
