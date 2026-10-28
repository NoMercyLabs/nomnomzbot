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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Stage;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stage;

/// <summary>
/// The <c>stage</c> system surface is provisioned like <c>tts_caption</c>: the catalogue seeds its gallery item, the
/// onboarding seed and <c>EnsureSystemWidgetAsync</c> create the channel's one widget (a second call returns the
/// same one), and the gallery listing never shows it.
/// </summary>
public sealed class StageSurfaceProvisioningTests
{
    private static readonly FakeTimeProvider Clock = new(
        new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero)
    );
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewWidgetService(WidgetTestDbContext db)
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
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<IPlaybackStateReader>()
        );
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

    private static async Task<WidgetSqliteTestDatabase> SeededCatalogueAsync()
    {
        WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await using WidgetTestDbContext db = database.NewContext();
        await new FirstPartyWidgetCatalogueSeeder(db).SeedAsync();
        await db.SaveChangesAsync();
        return database;
    }

    [Fact]
    public void Stage_is_a_system_surface_listening_for_the_one_stage_effect_event_on_the_full_canvas()
    {
        FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys.Should().Contain(StageSurface.WidgetKey);

        FirstPartyWidgetDefinition stage = FirstPartyWidgetCatalogue.All.Single(w =>
            w.Key == StageSurface.WidgetKey
        );
        stage.DefaultEventSubscriptions.Should().Equal(StageSurface.EventType);
        stage.ObsSetup.Should().Be(new ObsBrowserSource(1920, 1080, false, false));
        stage.DefaultSettings["intensity"].Should().Be(5);
        stage.DefaultSettings["accentColor"].Should().Be("#9146ff");
    }

    [Fact]
    public async Task Ensure_creates_the_stage_widget_once_per_channel_and_a_second_call_returns_the_same_one()
    {
        using WidgetSqliteTestDatabase database = await SeededCatalogueAsync();
        Guid channel = await SeedChannelAsync(database);

        Guid firstId;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> first = await NewWidgetService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), StageSurface.WidgetKey);
            first.IsSuccess.Should().BeTrue(first.ErrorMessage);
            first.Value.Name.Should().Be("Stage");
            first.Value.ActiveVersionId.Should().NotBeNull();
            first.Value.ObsSetup.Should().Be(new ObsBrowserSource(1920, 1080, false, false));
            first.Value.Settings["intensity"].Should().NotBeNull();
            firstId = first.Value.Id;
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Result<WidgetDetail> second = await NewWidgetService(db)
                .EnsureSystemWidgetAsync(channel.ToString(), StageSurface.WidgetKey);
            second.Value.Id.Should().Be(firstId);
            (await db.Widgets.CountAsync(w => w.BroadcasterId == channel)).Should().Be(1);
            Widget widget = await db.Widgets.SingleAsync(w => w.BroadcasterId == channel);
            widget.EventSubscriptions.Should().Equal(StageSurface.EventType);
        }
    }

    [Fact]
    public async Task The_gallery_listing_excludes_the_stage_surface()
    {
        using WidgetSqliteTestDatabase database = await SeededCatalogueAsync();

        await using WidgetTestDbContext db = database.NewContext();
        WidgetGalleryService gallery = new(db, new RecordingEventBus(), TimeProvider.System);
        Result<PagedList<GalleryItemSummary>> listed = await gallery.ListAsync(
            new(),
            new PaginationParams(PageSize: 100)
        );

        listed.IsSuccess.Should().BeTrue(listed.ErrorMessage);
        listed.Value.Items.Should().NotBeEmpty();
        listed.Value.Items.Should().NotContain(i => i.Name == "Stage");
        (await db.WidgetGalleryItems.AnyAsync(i => i.NaturalKey == StageSurface.WidgetKey))
            .Should()
            .BeTrue("the row exists so EnsureSystemWidgetAsync can find it");
    }

    [Fact]
    public async Task The_onboarding_seed_asks_for_the_stage_widget()
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        Guid broadcasterId = Guid.CreateVersion7();
        widgets
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                StageSurface.WidgetKey,
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<WidgetDetail>("stop", "STOP"));
        SystemWidgetSeedOnOnboardingHandler handler = new(
            widgets,
            NullLogger<SystemWidgetSeedOnOnboardingHandler>.Instance
        );

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = broadcasterId,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "12345",
                Name = "teststreamer",
            }
        );

        await widgets
            .Received(1)
            .EnsureSystemWidgetAsync(
                broadcasterId.ToString(),
                StageSurface.WidgetKey,
                Arg.Any<CancellationToken>()
            );
    }
}
