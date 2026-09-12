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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Per-widget overlay tokens + rotation with a grace window (audit B5, BUILD-TODO "individual tokens per
/// widget / rotatable tokens"). Proves: every widget mints its OWN token (never the channel-wide one); rotating
/// ONE widget's token never touches another widget's token or resolution (the actual blast-radius bug — every
/// browser source going blank on any rotation); the retired token keeps resolving through the stated grace
/// window and genuinely stops after it; and the rotation result carries the real before/after URLs.
/// </summary>
public sealed class WidgetServiceOverlayTokenRotationTests
{
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(WidgetTestDbContext db, FakeTimeProvider clock) =>
        new(
            db,
            EmptyConfig,
            Substitute.For<IEventBus>(),
            Substitute.For<IWidgetBuildService>(),
            new WidgetSettingsSchemaProvider(),
            clock,
            Substitute.For<NomNomzBot.Application.Music.Services.IMusicService>(),
            Substitute.For<NomNomzBot.Application.Contracts.CustomCode.IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

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
                OverlayToken = "channel-wide-tok",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<Widget> SeedWidgetAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        string name
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        Widget widget = new()
        {
            BroadcasterId = channelId,
            Name = name,
            Framework = "vanilla",
            Source = "custom",
            IsEnabled = true,
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget;
    }

    [Fact]
    public async Task Two_widgets_on_the_same_channel_get_genuinely_different_overlay_tokens()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);

        Widget widgetA = await SeedWidgetAsync(database, channel, "Alerts");
        Widget widgetB = await SeedWidgetAsync(database, channel, "Now Playing");

        widgetA.OverlayToken.Should().NotBeNullOrWhiteSpace();
        widgetB.OverlayToken.Should().NotBeNullOrWhiteSpace();
        widgetA.OverlayToken.Should().NotBe(widgetB.OverlayToken);
        // Never derived from / equal to the channel-wide token either.
        widgetA.OverlayToken.Should().NotBe("channel-wide-tok");
    }

    [Fact]
    public async Task Rotating_widget_As_token_does_not_invalidate_widget_Bs_active_resolution()
    {
        // The actual bug this slice fixes: rotating one widget used to kill EVERY widget's browser source
        // because they all shared one channel-wide token. Prove B keeps resolving to the same channel,
        // completely unaffected by A's rotation, using the exact lookup ticket issuance depends on.
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        FakeTimeProvider clock = new(new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Widget widgetA = await SeedWidgetAsync(database, channel, "Alerts");
        Widget widgetB = await SeedWidgetAsync(database, channel, "Now Playing");
        string widgetBTokenBefore = widgetB.OverlayToken;

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, clock);
            Result<WidgetTokenRotationResult> rotate = await service.RotateOverlayTokenAsync(
                channel.ToString(),
                widgetA.Id.ToString()
            );
            rotate.IsSuccess.Should().BeTrue(rotate.ErrorMessage);
        }

        await using WidgetTestDbContext read = database.NewContext();
        WidgetService reader = NewService(read, clock);

        // B's original token still resolves to the SAME channel — completely untouched by A's rotation.
        Guid? resolvedForB = await reader.ResolveBroadcasterIdByOverlayTokenAsync(
            widgetBTokenBefore
        );
        resolvedForB.Should().Be(channel);

        Widget stillB = await read.Widgets.SingleAsync(w => w.Id == widgetB.Id);
        stillB.OverlayToken.Should().Be(widgetBTokenBefore);
        stillB.PreviousOverlayToken.Should().BeNull();
    }

    [Fact]
    public async Task Rotation_mints_a_new_token_and_the_retired_one_resolves_through_the_grace_window()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        FakeTimeProvider clock = new(new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);
        Widget widget = await SeedWidgetAsync(database, channel, "Alerts");
        string oldToken = widget.OverlayToken;

        WidgetTokenRotationResult rotation;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, clock);
            Result<WidgetTokenRotationResult> result = await service.RotateOverlayTokenAsync(
                channel.ToString(),
                widget.Id.ToString()
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            rotation = result.Value;
        }

        rotation.PreviousUrl.Should().Contain(oldToken);
        rotation.NewUrl.Should().NotContain(oldToken);
        rotation.PreviousUrl.Should().NotBe(rotation.NewUrl);
        rotation.GraceExpiresAt.Should().Be(clock.GetUtcNow().AddMinutes(15));

        await using WidgetTestDbContext read1 = database.NewContext();
        WidgetService readerAtRotation = NewService(read1, clock);

        // Still inside the grace window: BOTH the old and the new token resolve to the channel.
        (await readerAtRotation.ResolveBroadcasterIdByOverlayTokenAsync(oldToken))
            .Should()
            .Be(channel);
        Widget rotated = await read1.Widgets.SingleAsync(w => w.Id == widget.Id);
        (await readerAtRotation.ResolveBroadcasterIdByOverlayTokenAsync(rotated.OverlayToken))
            .Should()
            .Be(channel);
        rotated.OverlayToken.Should().NotBe(oldToken);

        // Advance PAST the 15-minute grace window: the old token genuinely stops working; the new one keeps
        // working forever (no expiry on the live token).
        clock.Advance(TimeSpan.FromMinutes(16));
        await using WidgetTestDbContext read2 = database.NewContext();
        WidgetService readerAfterGrace = NewService(read2, clock);

        (await readerAfterGrace.ResolveBroadcasterIdByOverlayTokenAsync(oldToken))
            .Should()
            .BeNull();
        (await readerAfterGrace.ResolveBroadcasterIdByOverlayTokenAsync(rotated.OverlayToken))
            .Should()
            .Be(channel);
    }

    [Fact]
    public async Task Rotate_fails_NOT_FOUND_for_an_unknown_widget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        FakeTimeProvider clock = new(new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero));
        Guid channel = Guid.CreateVersion7();
        await SeedChannelAsync(database, channel);

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, clock);

        Result<WidgetTokenRotationResult> result = await service.RotateOverlayTokenAsync(
            channel.ToString(),
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
