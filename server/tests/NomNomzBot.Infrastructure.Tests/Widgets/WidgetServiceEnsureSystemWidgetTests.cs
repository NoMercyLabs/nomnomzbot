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
/// S052 (widgets-overlays.md §1.2): the TTS system surface is a channel-owned page "provisioned for every
/// channel at channel creation (and on first use if missing)" — never something a streamer installs from the
/// gallery. <c>EnsureSystemWidgetAsync</c> is the get-or-create that backs that promise for the <c>tts_caption</c>
/// system widget: absent, it installs the widget from its gallery item (compiled + live) exactly like a manual
/// gallery install would; present, it is idempotent and never creates a second widget or re-bumps the install
/// count.
/// </summary>
public sealed class WidgetServiceEnsureSystemWidgetTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(WidgetTestDbContext db)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("BUNDLE", "hash", "")));
        return new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            build,
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db)
        );
    }

    private static async Task SeedChannelAsync(WidgetSqliteTestDatabase database, Guid channelId)
    {
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
    }

    private static async Task<Guid> SeedTtsGalleryItemAsync(WidgetSqliteTestDatabase database)
    {
        Guid id = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new()
            {
                Id = id,
                Name = "TTS Caption",
                Description = "System TTS surface",
                Framework = "vue",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = "tts_caption",
                SourceCode = "TTS_SOURCE",
                ReviewStatus = "verified",
                AvailableInSaaS = true,
                DefaultEventSubscriptions = [],
                DefaultSettings = new() { ["showText"] = true },
            }
        );
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Ensure_on_a_fresh_channel_installs_the_system_widget_without_any_gallery_browsing_call()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Guid galleryItem = await SeedTtsGalleryItemAsync(database);

        WidgetDetail detail;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> result = await NewService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), "tts_caption");
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            detail = result.Value;
        }

        detail.Name.Should().Be("TTS Caption");
        detail.Source.Should().Be("first_party");
        detail.ActiveVersionId.Should().NotBeNull(); // compiled -> immediately live, an OBS-loadable overlay URL
        detail.OverlayUrl.Should().Contain(detail.Id.ToString());

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget widget = await db.Widgets.SingleAsync(w => w.BroadcasterId == channel);
            widget.GalleryItemId.Should().Be(galleryItem);
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.InstallCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task Ensure_called_twice_is_idempotent_returns_the_same_widget_never_double_installs()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Guid galleryItem = await SeedTtsGalleryItemAsync(database);

        Guid firstId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> first = await NewService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), "tts_caption");
            first.IsSuccess.Should().BeTrue(first.ErrorMessage);
            firstId = first.Value.Id;
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> second = await NewService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), "tts_caption");
            second.IsSuccess.Should().BeTrue(second.ErrorMessage);
            second.Value.Id.Should().Be(firstId);
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            (await db.Widgets.CountAsync(w => w.BroadcasterId == channel)).Should().Be(1);
            WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i =>
                i.Id == galleryItem
            );
            item.InstallCount.Should().Be(1); // second call never re-installs / re-bumps
        }
    }

    [Fact]
    public async Task Ensure_fails_honestly_when_no_gallery_item_carries_that_natural_key()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);

        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> result = await NewService(db)
            .EnsureSystemWidgetAsync(channel.ToString(), "tts_caption");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
