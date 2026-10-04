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
/// Creating a widget refuses a framework that has no build and runtime support (react, svelte) with the same
/// <c>WIDGET_FRAMEWORK_UNSUPPORTED</c> code the build and the gallery use, and stores nothing. Supported frameworks
/// still create a persisted widget.
/// </summary>
public sealed class WidgetServiceCreateFrameworkTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));

    private static WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            Substitute.For<IWidgetBuildService>(),
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

    [Theory]
    [InlineData("react")]
    [InlineData("svelte")]
    [InlineData("React")]
    public async Task Create_unsupported_framework_fails_with_the_framework_code_and_stores_no_widget(
        string framework
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await using WidgetTestDbContext db = database.NewContext();

        Result<WidgetDetail> created = await NewService(db)
            .CreateAsync(channel.ToString(), new() { Name = "Mine", Framework = framework });

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("WIDGET_FRAMEWORK_UNSUPPORTED");
        created.ErrorMessage.Should().Contain(framework);
        (await db.Widgets.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("vue")]
    [InlineData("vanilla")]
    public async Task Create_supported_framework_stores_the_widget_with_that_framework(
        string framework
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        await using WidgetTestDbContext db = database.NewContext();

        Result<WidgetDetail> created = await NewService(db)
            .CreateAsync(channel.ToString(), new() { Name = "Mine", Framework = framework });

        created.IsSuccess.Should().BeTrue();
        (await db.Widgets.SingleAsync()).Framework.Should().Be(framework);
    }
}
