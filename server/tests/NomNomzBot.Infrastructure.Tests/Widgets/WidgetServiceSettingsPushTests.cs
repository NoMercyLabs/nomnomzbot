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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// An open overlay reads its settings once, at page load. Live 2026-10-01 a saved reward id never reached the
/// open BSOD overlay, so it ignored the redemption. A settings save must announce the saved settings; a save
/// that leaves the settings alone must not.
/// </summary>
public sealed class WidgetServiceSettingsPushTests
{
    private static readonly Guid ChannelId = Guid.CreateVersion7();
    private static readonly Guid WidgetId = Guid.CreateVersion7();

    [Fact]
    public async Task A_settings_save_announces_the_saved_settings_for_the_open_overlay()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        IEventBus bus = Substitute.For<IEventBus>();

        Result<WidgetDetail> result = await NewService(db, bus)
            .UpdateAsync(
                ChannelId.ToString(),
                WidgetId.ToString(),
                new()
                {
                    Settings = new() { ["rewardId"] = "67b5638d", ["muteInputName"] = "Mic/Aux" },
                }
            );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetSettingsChangedEvent>(e =>
                    e.BroadcasterId == ChannelId
                    && e.WidgetId == WidgetId
                    && (string)e.Settings["rewardId"] == "67b5638d"
                    && (string)e.Settings["muteInputName"] == "Mic/Aux"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_save_that_leaves_the_settings_alone_announces_nothing_to_the_overlay()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        IEventBus bus = Substitute.For<IEventBus>();

        await NewService(db, bus)
            .UpdateAsync(ChannelId.ToString(), WidgetId.ToString(), new() { Name = "Renamed" });

        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<WidgetSettingsChangedEvent>(), Arg.Any<CancellationToken>());
    }

    private static WidgetService NewService(WidgetTestDbContext db, IEventBus bus) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            bus,
            Substitute.For<IWidgetBuildService>(),
            new WidgetSettingsSchemaProvider(),
            new FakeTimeProvider(),
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    private static async Task SeedAsync(WidgetSqliteTestDatabase database)
    {
        await using WidgetTestDbContext db = database.NewContext();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
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
                Id = WidgetId,
                BroadcasterId = ChannelId,
                Name = "BSOD",
                Framework = "vue",
                Source = "custom",
                IsEnabled = true,
                Settings = new() { ["rewardId"] = "" },
                EventSubscriptions = ["reward_redeemed"],
            }
        );
        await db.SaveChangesAsync();
    }
}
