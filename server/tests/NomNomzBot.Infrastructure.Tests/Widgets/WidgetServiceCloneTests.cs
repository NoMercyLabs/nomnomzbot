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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior tests for clone-to-edit and install-from-gallery: a clone is a NEW, fully-detached custom widget whose
/// source (from an installed widget OR a verified gallery item) is copied + compiled so it is immediately live; an
/// install is a tracked instance linked to the gallery item (tier-inheriting, install-count-bumping). Invalid fork
/// sources and unverified gallery items fail honestly.
/// </summary>
public sealed class WidgetServiceCloneTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(WidgetTestDbContext db)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("BUNDLE", "hash", "")));
        return new WidgetService(db, EmptyConfig, Substitute.For<IEventBus>(), build, Clock);
    }

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
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
    }

    private static async Task SeedWidgetAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        Guid widgetId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Widgets.Add(
            new Widget
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "Alerts",
                Description = "My alert box",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
                EventSubscriptions = ["follow"],
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task CompileAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        Guid widgetId,
        string source
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetVersionDetail> r = await NewService(db)
            .CompileAsync(
                channelId.ToString(),
                widgetId.ToString(),
                new CompileWidgetRequest { SourceCode = source }
            );
        r.IsSuccess.Should().BeTrue(r.ErrorMessage);
    }

    [Fact]
    public async Task Clone_forks_an_installed_widget_into_a_new_live_custom_widget_with_the_copied_source()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await CompileAsync(database, channel, widget, "SOURCE_V1");

        WidgetDetail clone;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db)
                .CloneToEditAsync(
                    channel.ToString(),
                    new CloneWidgetRequest { InstalledWidgetId = widget }
                );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            clone = result.Value;
        }

        clone.Id.Should().NotBe(widget); // a distinct, new widget
        clone.Name.Should().Be("Copy of Alerts");
        clone.Description.Should().Be("My alert box");
        clone.Framework.Should().Be("vanilla");
        clone.Source.Should().Be("custom");
        clone.EventSubscriptions.Should().Contain("follow");
        clone.ActiveVersionId.Should().NotBeNull(); // compiled -> immediately live

        // The clone owns its own version carrying the copied source (independent of the original).
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion cloneVersion = await db.WidgetVersions.SingleAsync(v =>
                v.WidgetId == clone.Id
            );
            cloneVersion.SourceCode.Should().Be("SOURCE_V1");
            cloneVersion.BuildStatus.Should().Be("success");
            cloneVersion.Id.Should().Be(clone.ActiveVersionId!.Value);
        }
    }

    [Theory]
    [InlineData(false, false)] // neither
    [InlineData(true, true)] // both
    public async Task Clone_requires_exactly_one_fork_source(bool gallery, bool installed)
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .CloneToEditAsync(
                channel.ToString(),
                new CloneWidgetRequest
                {
                    GalleryItemId = gallery ? Guid.CreateVersion7() : null,
                    InstalledWidgetId = installed ? Guid.CreateVersion7() : null,
                }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_CLONE_SOURCE_INVALID");
    }

    private static async Task<Guid> SeedGalleryItemAsync(
        WidgetSqliteTestDatabase database,
        string reviewStatus = "verified",
        string trustTier = "first_party"
    )
    {
        Guid id = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new WidgetGalleryItem
            {
                Id = id,
                Name = "Alerts",
                Description = "First-party alerts",
                Framework = "vanilla",
                TrustTier = trustTier,
                SourceKind = "in_repo",
                NaturalKey = "alerts",
                SourceCode = "GALLERY_SRC",
                ReviewStatus = reviewStatus,
                AvailableInSaaS = true,
                DefaultEventSubscriptions = ["follow", "cheer"],
                DefaultSettings = new Dictionary<string, object> { ["durationMs"] = 6000 },
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Clone_from_a_verified_gallery_item_creates_a_detached_live_custom_widget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Guid galleryItem = await SeedGalleryItemAsync(database);

        WidgetDetail clone;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db)
                .CloneToEditAsync(
                    channel.ToString(),
                    new CloneWidgetRequest { GalleryItemId = galleryItem }
                );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            clone = result.Value;
        }

        clone.Name.Should().Be("Copy of Alerts");
        clone.Source.Should().Be("custom"); // a fork is detached + custom (=> unverified trust tier)
        clone.Framework.Should().Be("vanilla");
        clone.EventSubscriptions.Should().Contain("follow");
        clone.ActiveVersionId.Should().NotBeNull(); // compiled -> immediately live

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion version = await db.WidgetVersions.SingleAsync(v =>
                v.WidgetId == clone.Id
            );
            version.SourceCode.Should().Be("GALLERY_SRC");
            // A clone is fully detached — it never bumps the gallery item's install count.
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.InstallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task Install_from_gallery_creates_a_linked_live_widget_and_bumps_install_count()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Guid galleryItem = await SeedGalleryItemAsync(database, trustTier: "first_party");

        WidgetDetail installed;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db)
                .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            installed = result.Value;
        }

        installed.Name.Should().Be("Alerts");
        installed.Source.Should().Be("first_party"); // drives the derived first_party trust tier
        installed.ActiveVersionId.Should().NotBeNull(); // compiled on install -> live

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget widget = await db.Widgets.SingleAsync(w => w.Id == installed.Id);
            widget.GalleryItemId.Should().Be(galleryItem); // a tracked instance linked to the catalogue entry
            WidgetVersion version = await db.WidgetVersions.SingleAsync(v =>
                v.WidgetId == installed.Id
            );
            version.SourceCode.Should().Be("GALLERY_SRC");
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.InstallCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Install_from_an_unverified_gallery_item_fails()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Guid galleryItem = await SeedGalleryItemAsync(database, reviewStatus: "submitted");

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_GALLERY_ITEM_NOT_VERIFIED");
    }

    [Fact]
    public async Task Clone_a_widget_with_no_compiled_source_fails()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget); // never compiled -> no source

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .CloneToEditAsync(
                channel.ToString(),
                new CloneWidgetRequest { InstalledWidgetId = widget }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_NO_SOURCE");
    }

    [Fact]
    public async Task Clone_a_widget_owned_by_another_channel_is_not_found()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        Guid widget = Guid.CreateVersion7();
        Guid otherChannel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        await SeedWidgetAsync(database, channel, widget);
        await CompileAsync(database, channel, widget, "SRC");
        await using (WidgetTestDbContext seed = database.NewContext())
        {
            seed.Channels.Add(
                new Channel
                {
                    Id = otherChannel,
                    OwnerUserId = Guid.CreateVersion7(),
                    TwitchChannelId = "999",
                    Name = "other",
                    NameNormalized = "other",
                    OverlayToken = "tok2",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .CloneToEditAsync(
                otherChannel.ToString(),
                new CloneWidgetRequest { InstalledWidgetId = widget }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
