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
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior tests for the widget compile-on-save service. Each proves a consequence of an action — the version
/// row that lands and its build outcome, the active-version pointer that moves (or does not), the lifecycle event
/// emitted — not merely that a call returned non-null. Runs on real SQLite so the append-only unique
/// <c>(WidgetId, VersionNumber)</c> constraint and the JSON-mapped widget columns are genuinely exercised, with a
/// fresh context per logical step so every assertion reads persisted state rather than a tracked entity.
/// </summary>
public sealed class WidgetServiceCompileTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    private static WidgetService NewService(
        WidgetTestDbContext db,
        IEventBus eventBus,
        IWidgetBuildService buildService
    ) => new(db, EmptyConfig, eventBus, buildService, Clock);

    private static Result<WidgetBuildOutput> Ok(
        string bundle = "BUNDLE",
        string hash = "hash123"
    ) => Result.Success(new WidgetBuildOutput(bundle, hash, ""));

    private static Result<WidgetBuildOutput> Fail() =>
        Result.Failure<WidgetBuildOutput>("boom", "WIDGET_BUILD_FAILED");

    private static IWidgetBuildService BuildReturning(Result<WidgetBuildOutput> output)
    {
        IWidgetBuildService build = Substitute.For<IWidgetBuildService>();
        build.BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>()).Returns(output);
        return build;
    }

    private static async Task<Guid> SeedChannelAsync(WidgetSqliteTestDatabase database)
    {
        Guid channelId = Guid.CreateVersion7();
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
        return channelId;
    }

    private static async Task<Guid> SeedWidgetAsync(
        WidgetSqliteTestDatabase database,
        Guid channelId,
        string framework = "vanilla"
    )
    {
        Guid widgetId = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.Widgets.Add(
            new Widget
            {
                Id = widgetId,
                BroadcasterId = channelId,
                Name = "Alerts",
                Framework = framework,
                Source = "custom",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        return widgetId;
    }

    /// <summary>Runs one compile in its own context (own event bus) and returns the recorded version detail.</summary>
    private static async Task<WidgetVersionDetail> CompileAsync(
        WidgetSqliteTestDatabase database,
        Guid channel,
        Guid widget,
        string source,
        Result<WidgetBuildOutput> output
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>(), BuildReturning(output));
        Result<WidgetVersionDetail> result = await service.CompileAsync(
            channel.ToString(),
            widget.ToString(),
            new CompileWidgetRequest { SourceCode = source }
        );
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        return result.Value;
    }

    [Fact]
    public async Task CompileAsync_Success_RecordsVersionOne_PointsActive_AndPublishesSucceeded()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);
        IEventBus bus = Substitute.For<IEventBus>();
        IWidgetBuildService build = BuildReturning(Ok());

        WidgetVersionDetail detail;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, build);
            Result<WidgetVersionDetail> result = await service.CompileAsync(
                channel.ToString(),
                widget.ToString(),
                new CompileWidgetRequest { SourceCode = "export const x = 1;" }
            );

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            detail = result.Value;
        }

        // The returned detail reports the build outcome — the version is #1 and built.
        detail.VersionNumber.Should().Be(1);
        detail.BuildStatus.Should().Be("success");

        // The persisted version row carries the full compiled shape, and the widget now points at it.
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion stored = await db.WidgetVersions.SingleAsync(v => v.WidgetId == widget);
            stored.Id.Should().Be(detail.Id);
            stored.VersionNumber.Should().Be(1);
            stored.BuildStatus.Should().Be("success");
            stored.SourceCode.Should().Be("export const x = 1;");
            stored.CompiledBundle.Should().Be("BUNDLE");
            stored.ContentHash.Should().Be("hash123");
            stored.BuildError.Should().BeNull();
            stored.CompiledAt.Should().Be(Clock.GetUtcNow().UtcDateTime);

            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().Be(stored.Id);
        }

        // The success event fired once, carrying the tenant, widget, assigned number, and cache-bust hash.
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetBuildSucceededEvent>(e =>
                    e.BroadcasterId == channel
                    && e.WidgetId == widget
                    && e.VersionId == detail.Id
                    && e.VersionNumber == 1
                    && e.ContentHash == "hash123"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CompileAsync_BuildFailure_RecordsErrorVersion_LeavesActiveNull_AndPublishesFailed()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);
        IEventBus bus = Substitute.For<IEventBus>();
        IWidgetBuildService build = BuildReturning(Fail());

        Result<WidgetVersionDetail> result;
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, build);
            result = await service.CompileAsync(
                channel.ToString(),
                widget.ToString(),
                new CompileWidgetRequest { SourceCode = "bad(" }
            );
        }

        // The version was recorded, so the service call itself succeeds — the outcome lives in BuildStatus.
        result.IsSuccess.Should().BeTrue();
        result.Value.BuildStatus.Should().Be("error");

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetVersion stored = await db.WidgetVersions.SingleAsync(v => v.WidgetId == widget);
            stored.VersionNumber.Should().Be(1);
            stored.BuildStatus.Should().Be("error");
            stored.BuildError.Should().Contain("boom");
            stored.CompiledBundle.Should().BeNull();
            stored.ContentHash.Should().BeNull();
            stored.CompiledAt.Should().BeNull();

            // A failed build never moves the active pointer — the widget still has no live version.
            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().BeNull();
        }

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetBuildFailedEvent>(e =>
                    e.BroadcasterId == channel
                    && e.WidgetId == widget
                    && e.VersionNumber == 1
                    && e.BuildError == "boom"
                ),
                Arg.Any<CancellationToken>()
            );
        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<WidgetBuildSucceededEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompileAsync_ThreeSuccesses_NumberSequentially_ActiveTracksLatest()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);

        WidgetVersionDetail v1 = await CompileAsync(database, channel, widget, "v1", Ok());
        WidgetVersionDetail v2 = await CompileAsync(database, channel, widget, "v2", Ok());
        WidgetVersionDetail v3 = await CompileAsync(database, channel, widget, "v3", Ok());

        new[] { v1.VersionNumber, v2.VersionNumber, v3.VersionNumber }.Should().Equal(1, 2, 3);

        await using WidgetTestDbContext db = database.NewContext();
        List<int> stored = await db
            .WidgetVersions.Where(v => v.WidgetId == widget)
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.VersionNumber)
            .ToListAsync();
        stored.Should().Equal(1, 2, 3);

        Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
        storedWidget.ActiveVersionId.Should().Be(v3.Id);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsNewestFirst()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);

        await CompileAsync(database, channel, widget, "v1", Ok());
        await CompileAsync(database, channel, widget, "v2", Ok());
        await CompileAsync(database, channel, widget, "v3", Ok());

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>(), BuildReturning(Ok()));
        Result<PagedList<WidgetVersionSummary>> result = await service.ListVersionsAsync(
            channel.ToString(),
            widget.ToString(),
            new PaginationParams()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Select(i => i.VersionNumber).Should().Equal(3, 2, 1);
    }

    [Fact]
    public async Task ListVersionsAsync_NotFound_ForUnknownWidgetOrWrongTenant()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);
        Guid otherChannel = await SeedChannelAsync(database);
        await CompileAsync(database, channel, widget, "v1", Ok());

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>(), BuildReturning(Ok()));

        // A widget id that does not exist.
        Result<PagedList<WidgetVersionSummary>> unknown = await service.ListVersionsAsync(
            channel.ToString(),
            Guid.NewGuid().ToString(),
            new PaginationParams()
        );
        unknown.IsFailure.Should().BeTrue();
        unknown.ErrorCode.Should().Be("NOT_FOUND");

        // The real widget, but asked for by a tenant that does not own it.
        Result<PagedList<WidgetVersionSummary>> wrongTenant = await service.ListVersionsAsync(
            otherChannel.ToString(),
            widget.ToString(),
            new PaginationParams()
        );
        wrongTenant.IsFailure.Should().BeTrue();
        wrongTenant.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetVersionAsync_ReturnsSourceCode_AndNotFoundForUnknownVersion()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);
        WidgetVersionDetail v1 = await CompileAsync(
            database,
            channel,
            widget,
            "export const y = 2;",
            Ok()
        );

        await using WidgetTestDbContext db = database.NewContext();
        WidgetService service = NewService(db, Substitute.For<IEventBus>(), BuildReturning(Ok()));

        Result<WidgetVersionDetail> got = await service.GetVersionAsync(
            channel.ToString(),
            widget.ToString(),
            v1.Id.ToString()
        );
        got.IsSuccess.Should().BeTrue(got.ErrorMessage);
        got.Value.Id.Should().Be(v1.Id);
        got.Value.VersionNumber.Should().Be(1);
        got.Value.SourceCode.Should().Be("export const y = 2;");

        Result<WidgetVersionDetail> missing = await service.GetVersionAsync(
            channel.ToString(),
            widget.ToString(),
            Guid.NewGuid().ToString()
        );
        missing.IsFailure.Should().BeTrue();
        missing.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task RollbackAsync_ToSuccessfulVersion_RepointsActive_AndPublishesSucceeded()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);

        WidgetVersionDetail v1 = await CompileAsync(
            database,
            channel,
            widget,
            "v1",
            Ok("BUNDLE1", "hash1")
        );
        await CompileAsync(database, channel, widget, "v2", Ok("BUNDLE2", "hash2"));

        IEventBus bus = Substitute.For<IEventBus>();
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, BuildReturning(Ok()));
            Result<WidgetDetail> result = await service.RollbackAsync(
                channel.ToString(),
                widget.ToString(),
                v1.Id.ToString()
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.Value.ActiveVersionId.Should().Be(v1.Id);
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().Be(v1.Id);
        }

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetBuildSucceededEvent>(e =>
                    e.WidgetId == widget
                    && e.VersionId == v1.Id
                    && e.VersionNumber == 1
                    && e.ContentHash == "hash1"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RollbackAsync_ToUnsuccessfulVersion_Fails_AndLeavesActiveUnchanged()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);

        WidgetVersionDetail v1 = await CompileAsync(database, channel, widget, "v1", Ok());
        // A failed build is a persisted `error` version — a valid rollback target id, but not a successful build.
        WidgetVersionDetail errorVersion = await CompileAsync(
            database,
            channel,
            widget,
            "bad(",
            Fail()
        );
        errorVersion.BuildStatus.Should().Be("error");

        IEventBus bus = Substitute.For<IEventBus>();
        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, BuildReturning(Ok()));
            Result<WidgetDetail> result = await service.RollbackAsync(
                channel.ToString(),
                widget.ToString(),
                errorVersion.Id.ToString()
            );
            result.IsFailure.Should().BeTrue();
            result.ErrorCode.Should().Be("WIDGET_VERSION_NOT_SUCCESSFUL");
        }

        // The active pointer still sits on the last successful build (v1), untouched by the rejected rollback.
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.ActiveVersionId.Should().Be(v1.Id);
        }

        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<WidgetBuildSucceededEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordRuntimeErrorAsync_StampsErrorAndLastRan_AndNotFoundForUnknownWidget()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        Guid channel = await SeedChannelAsync(database);
        Guid widget = await SeedWidgetAsync(database, channel);
        IEventBus bus = Substitute.For<IEventBus>();

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, BuildReturning(Ok()));
            Result result = await service.RecordRuntimeErrorAsync(
                channel.ToString(),
                widget.ToString(),
                "TypeError: x is undefined"
            );
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget storedWidget = await db.Widgets.SingleAsync(w => w.Id == widget);
            storedWidget.LastRuntimeError.Should().Be("TypeError: x is undefined");
            storedWidget.LastRanAt.Should().Be(Clock.GetUtcNow().UtcDateTime);
        }

        await using (WidgetTestDbContext db = database.NewContext())
        {
            WidgetService service = NewService(db, bus, BuildReturning(Ok()));
            Result missing = await service.RecordRuntimeErrorAsync(
                channel.ToString(),
                Guid.NewGuid().ToString(),
                "irrelevant"
            );
            missing.IsFailure.Should().BeTrue();
            missing.ErrorCode.Should().Be("NOT_FOUND");
        }
    }
}
