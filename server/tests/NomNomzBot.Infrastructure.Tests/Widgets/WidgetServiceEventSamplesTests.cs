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
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// An event sample the author edited is a project file <c>events/&lt;type&gt;.json</c>: a save stores it with the
/// version, the next open returns it, a save without it is the reset to the stock sample, and a file that is not a
/// JSON object is rejected before a version is stored.
/// </summary>
public sealed class WidgetServiceEventSamplesTests : IClassFixture<VueSfcCompilerFixture>
{
    private const string Html = "<div id=\"w\">hi</div>";

    private static readonly FakeTimeProvider Clock = new(new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly VueSfcCompilerFixture _fixture;

    public WidgetServiceEventSamplesTests(VueSfcCompilerFixture fixture) => _fixture = fixture;

    private WidgetService NewService(WidgetTestDbContext db) =>
        new(
            db,
            new ConfigurationBuilder().Build(),
            Substitute.For<IEventBus>(),
            new EsbuildWidgetBuildService(
                new ProcessRunner(),
                _fixture.Compiler,
                new WidgetDependencyAllowlist(),
                new ConfigurationBuilder().AddEnvironmentVariables().Build(),
                NullLogger<EsbuildWidgetBuildService>.Instance
            ),
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );

    private static async Task<(Guid Channel, Guid Widget)> SeedAsync(
        WidgetSqliteTestDatabase database
    )
    {
        Guid channelId = Guid.CreateVersion7();
        Guid widgetId = Guid.CreateVersion7();
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
        db.Widgets.Add(
            new()
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "My widget",
                Framework = "vanilla",
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return (channelId, widgetId);
    }

    private static ProjectDto Project(params (string Path, string Content)[] samples)
    {
        Dictionary<string, string> files = new() { ["index.html"] = Html };
        foreach ((string path, string content) in samples)
            files[path] = content;
        return new(files, new("index.html", "widget", "vanilla", []));
    }

    [Fact]
    public async Task An_edited_sample_is_returned_by_the_next_open_and_a_save_without_it_resets_the_type()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        const string edited = "{\n  \"user_name\": \"kitte\",\n  \"tier\": 2\n}";

        Result<WidgetVersionDetail> saved = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project(("events/follow.json", edited))
        );
        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        Result<ProjectDto> reopened = await service.GetProjectAsync(
            channel.ToString(),
            widget.ToString()
        );

        reopened.Value.Files.Keys.Should().BeEquivalentTo("index.html", "events/follow.json");
        reopened.Value.Files["events/follow.json"].Should().Be(edited);

        (await service.SaveProjectAsync(channel.ToString(), widget.ToString(), Project()))
            .IsSuccess.Should()
            .BeTrue();
        Result<ProjectDto> afterReset = await service.GetProjectAsync(
            channel.ToString(),
            widget.ToString()
        );

        afterReset.Value.Files.Keys.Should().BeEquivalentTo("index.html");
    }

    [Theory]
    [InlineData("{ \"user_name\": ")]
    [InlineData("[1, 2]")]
    public async Task A_sample_that_is_not_a_json_object_fails_the_save_and_stores_no_new_version(
        string content
    )
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        (Guid channel, Guid widget) = await SeedAsync(database);
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db);
        (await service.SaveProjectAsync(channel.ToString(), widget.ToString(), Project()))
            .IsSuccess.Should()
            .BeTrue();

        Result<WidgetVersionDetail> bad = await service.SaveProjectAsync(
            channel.ToString(),
            widget.ToString(),
            Project(("events/follow.json", content))
        );

        bad.IsFailure.Should().BeTrue();
        bad.ErrorCode.Should().Be("WIDGET_EVENT_SAMPLE_INVALID");
        bad.ErrorMessage.Should().Contain("events/follow.json");
        (await db.WidgetVersions.CountAsync(v => v.WidgetId == widget)).Should().Be(1);
    }
}
