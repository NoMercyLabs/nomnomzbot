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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A first-party widget the channel never edited follows the catalogue, but the rebuild must never hold the host
/// back: a deploy took over 180 s to answer /health/ready because startup rebuilt every channel's widget (about
/// 4 s each) inside the seed transaction. These tests run the real worker inside a real generic host over real
/// SQLite and the real <see cref="WidgetService"/>; only the build tool (held on a gate), the event bus and the
/// run-once lease are stand-ins.
/// </summary>
public sealed class InstalledWidgetSourceUpgradeWorkerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly FakeTimeProvider Clock = new(
        new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero)
    );
    private static readonly IConfiguration EmptyConfig = new ConfigurationBuilder().Build();

    [Fact]
    public void No_content_seeder_depends_on_the_widget_build_path()
    {
        List<Type> offenders =
        [
            .. typeof(NomNomzBot.Infrastructure.DependencyInjection)
                .Assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsClass: true })
                .Where(type => typeof(ISeeder).IsAssignableFrom(type))
                .Where(type =>
                    type.GetConstructors()
                        .SelectMany(ctor => ctor.GetParameters())
                        .Any(parameter =>
                            parameter.ParameterType == typeof(IWidgetService)
                            || parameter.ParameterType == typeof(IWidgetBuildService)
                        )
                ),
        ];

        offenders
            .Should()
            .BeEmpty(
                "seeders run in one transaction before the host listens; a widget build there delays /health/ready"
            );
    }

    [Fact]
    public async Task The_host_starts_while_the_rebuild_is_pending_and_the_widget_keeps_its_last_good_build()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid galleryItem = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid widgetId = await InstallAsync(database, build, bus, channel, galleryItem);
        await MoveCatalogueToV2Async(database, galleryItem);
        build.HoldV2();

        using IHost host = NewHost(
            database,
            build,
            bus,
            new ListLogger<InstalledWidgetSourceUpgradeWorker>()
        );
        await host.StartAsync().WaitAsync(Patience);

        await build.EnteredV2.Task.WaitAsync(Patience);
        InstalledWidgetSourceUpgradeWorker worker = WorkerOf(host);
        worker.ExecuteTask!.IsCompleted.Should().BeFalse("the rebuild is held on the gate");
        (await ServedBundleAsync(database, build, bus, widgetId)).Should().Be("compiled::V1");
        (await InstalledRevisionAsync(database, widgetId)).Should().Be(1);

        build.ReleaseV2();
        await worker.ExecuteTask.WaitAsync(Patience);

        (await ServedBundleAsync(database, build, bus, widgetId)).Should().Be("compiled::V2");
        (await InstalledRevisionAsync(database, widgetId)).Should().Be(2);
        await bus.Received(1)
            .PublishAsync(
                Arg.Is<WidgetBuildSucceededEvent>(e =>
                    e.WidgetId == widgetId && e.VersionNumber == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await host.StopAsync();
    }

    [Fact]
    public async Task Failed_rebuilds_log_once_per_channel_and_leave_the_host_running_on_the_old_build()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid alerts = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid recap = await SeedGalleryItemAsync(database, "raid_recap", "Raid Recap");
        Guid alertsWidget = await InstallAsync(database, build, bus, channel, alerts);
        Guid recapWidget = await InstallAsync(database, build, bus, channel, recap);
        await MoveCatalogueToV2Async(database, alerts);
        await MoveCatalogueToV2Async(database, recap);
        build.BreakEveryV2();

        ListLogger<InstalledWidgetSourceUpgradeWorker> log = new();
        using IHost host = NewHost(database, build, bus, log);
        await host.StartAsync().WaitAsync(Patience);
        await WorkerOf(host).ExecuteTask!.WaitAsync(Patience);

        build.V2Calls.Should().Be(4, "each widget is built, then retried once in the same pass");
        log.Entries.Where(e => e.Level >= LogLevel.Warning)
            .Should()
            .ContainSingle("two failed widgets of one channel are one log line")
            .Which.Message.Should()
            .Contain(channel.ToString());
        (await ServedBundleAsync(database, build, bus, alertsWidget)).Should().Be("compiled::V1");
        (await ServedBundleAsync(database, build, bus, recapWidget)).Should().Be("compiled::V1");
        (await InstalledRevisionAsync(database, alertsWidget))
            .Should()
            .Be(1, "a failed rebuild retries next start");
        (await InstalledRevisionAsync(database, recapWidget)).Should().Be(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task A_rebuild_that_fails_once_is_retried_after_the_others_and_goes_live_in_the_same_start()
    {
        // Live 2026-11-27: the chat box compile timed out under startup load and kept its old build until the
        // next deploy, while the same rebuild took 5 s once the box was quiet.
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid alerts = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid recap = await SeedGalleryItemAsync(database, "raid_recap", "Raid Recap");
        Guid alertsWidget = await InstallAsync(database, build, bus, channel, alerts);
        Guid recapWidget = await InstallAsync(database, build, bus, channel, recap);
        await MoveCatalogueToV2Async(database, alerts);
        await MoveCatalogueToV2Async(database, recap);
        build.FailV2(alertsFails: true, recapThrows: false);

        ListLogger<InstalledWidgetSourceUpgradeWorker> log = new();
        using IHost host = NewHost(database, build, bus, log);
        await host.StartAsync().WaitAsync(Patience);
        await WorkerOf(host).ExecuteTask!.WaitAsync(Patience);

        build.V2Calls.Should().Be(3, "alerts fails, raid recap builds, then alerts is retried");
        (await InstalledRevisionAsync(database, alertsWidget)).Should().Be(2);
        (await InstalledRevisionAsync(database, recapWidget)).Should().Be(2);
        (await ServedBundleAsync(database, build, bus, alertsWidget)).Should().Be("compiled::V2");
        log.Entries.Should()
            .NotContain(
                e => e.Level >= LogLevel.Warning,
                "the retry went live, nothing is left over"
            );
        await host.StopAsync();
    }

    [Fact]
    public async Task Rebuilds_only_stale_untouched_installs()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid item = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid stale = await InstallAsync(database, build, bus, channel, item);
        Guid customized = await InstallAsync(database, build, bus, channel, item);
        await using (WidgetTestDbContext db = database.NewContext())
        {
            db.WidgetVersions.Add(
                new()
                {
                    WidgetId = customized,
                    BroadcasterId = channel,
                    VersionNumber = 2,
                    BuildStatus = "success",
                    CompiledBundle = "compiled::MINE",
                    ContentHash = "h",
                }
            );
            await db.SaveChangesAsync();
        }
        await MoveCatalogueToV2Async(database, item);

        using IHost host = NewHost(
            database,
            build,
            bus,
            new ListLogger<InstalledWidgetSourceUpgradeWorker>()
        );
        await host.StartAsync().WaitAsync(Patience);
        await WorkerOf(host).ExecuteTask!.WaitAsync(Patience);

        (await InstalledRevisionAsync(database, stale)).Should().Be(2);
        (await InstalledRevisionAsync(database, customized)).Should().Be(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task Leaves_a_widget_the_channel_deleted_on_its_old_revision()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid item = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid live = await InstallAsync(database, build, bus, channel, item);
        Guid deleted = await InstallAsync(database, build, bus, channel, item);
        await using (WidgetTestDbContext db = database.NewContext())
        {
            Widget row = await db.Widgets.IgnoreQueryFilters().SingleAsync(w => w.Id == deleted);
            row.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await MoveCatalogueToV2Async(database, item);

        ListLogger<InstalledWidgetSourceUpgradeWorker> log = new();
        using IHost host = NewHost(database, build, bus, log);
        await host.StartAsync().WaitAsync(Patience);
        await WorkerOf(host).ExecuteTask!.WaitAsync(Patience);

        (await InstalledRevisionAsync(database, live)).Should().Be(2);
        (await InstalledRevisionAsync(database, deleted)).Should().Be(1);
        log.Entries.Should()
            .NotContain(
                e => e.Level >= LogLevel.Warning,
                "a deleted widget is not upgraded, so it never fails"
            );
        await host.StopAsync();
    }

    [Fact]
    public async Task A_rebuild_that_failed_is_tried_again_on_the_next_start()
    {
        using WidgetSqliteTestDatabase database = WidgetSqliteTestDatabase.Open();
        GatedBuild build = new();
        IEventBus bus = Substitute.For<IEventBus>();
        Guid channel = await SeedChannelAsync(database, "tok-a");
        Guid item = await SeedGalleryItemAsync(database, "alerts", "Alerts");
        Guid widget = await InstallAsync(database, build, bus, channel, item);
        await MoveCatalogueToV2Async(database, item);
        build.BreakEveryV2();

        using (
            IHost first = NewHost(
                database,
                build,
                bus,
                new ListLogger<InstalledWidgetSourceUpgradeWorker>()
            )
        )
        {
            await first.StartAsync().WaitAsync(Patience);
            await WorkerOf(first).ExecuteTask!.WaitAsync(Patience);
            await first.StopAsync();
        }
        (await InstalledRevisionAsync(database, widget)).Should().Be(1);
        build.FixV2();

        using IHost second = NewHost(
            database,
            build,
            bus,
            new ListLogger<InstalledWidgetSourceUpgradeWorker>()
        );
        await second.StartAsync().WaitAsync(Patience);
        await WorkerOf(second).ExecuteTask!.WaitAsync(Patience);

        (await InstalledRevisionAsync(database, widget))
            .Should()
            .Be(2, "the failed version never went live, so it is no edit of the channel's");
        (await ServedBundleAsync(database, build, bus, widget)).Should().Be("compiled::V2");
        await second.StopAsync();
    }

    private static InstalledWidgetSourceUpgradeWorker WorkerOf(IHost host) =>
        host
            .Services.GetServices<IHostedService>()
            .OfType<InstalledWidgetSourceUpgradeWorker>()
            .Single();

    private static IHost NewHost(
        WidgetSqliteTestDatabase database,
        GatedBuild build,
        IEventBus bus,
        ILogger<InstalledWidgetSourceUpgradeWorker> logger
    )
    {
        IRunOnceGuard guard = Substitute.For<IRunOnceGuard>();
        guard
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IAsyncDisposable?>(Substitute.For<IAsyncDisposable>()));

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IApplicationDbContext>(_ => database.NewContext());
        builder.Services.AddScoped<IWidgetService>(sp =>
            NewService(
                (WidgetTestDbContext)sp.GetRequiredService<IApplicationDbContext>(),
                build,
                bus
            )
        );
        builder.Services.AddSingleton(guard);
        builder.Services.AddSingleton(logger);
        builder.Services.AddHostedService<InstalledWidgetSourceUpgradeWorker>();
        return builder.Build();
    }

    private static WidgetService NewService(
        WidgetTestDbContext db,
        GatedBuild build,
        IEventBus bus
    ) =>
        new(
            db,
            EmptyConfig,
            bus,
            build.Tool,
            new WidgetSettingsSchemaProvider(),
            Clock,
            Substitute.For<IMusicService>(),
            Substitute.For<IScriptStorageService>(),
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>(),
            Substitute.For<NomNomzBot.Domain.Music.Interfaces.IPlaybackStateReader>()
        );

    private static async Task<Guid> SeedChannelAsync(
        WidgetSqliteTestDatabase database,
        string token
    )
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
                OverlayToken = token,
            }
        );
        await db.SaveChangesAsync();
        return channelId;
    }

    private static async Task<Guid> SeedGalleryItemAsync(
        WidgetSqliteTestDatabase database,
        string naturalKey,
        string name
    )
    {
        Guid id = Guid.CreateVersion7();
        await using WidgetTestDbContext db = database.NewContext();
        db.WidgetGalleryItems.Add(
            new()
            {
                Id = id,
                Name = name,
                Framework = "vue",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = naturalKey,
                SourceCode = "V1",
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

    private static async Task<Guid> InstallAsync(
        WidgetSqliteTestDatabase database,
        GatedBuild build,
        IEventBus bus,
        Guid channel,
        Guid galleryItem
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        Result<WidgetDetail> installed = await NewService(db, build, bus)
            .InstallFromGalleryAsync(channel.ToString(), galleryItem.ToString());
        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        return installed.Value.Id;
    }

    private static async Task MoveCatalogueToV2Async(
        WidgetSqliteTestDatabase database,
        Guid galleryItem
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        WidgetGalleryItem item = await db.WidgetGalleryItems.SingleAsync(i => i.Id == galleryItem);
        item.SourceCode = "V2";
        item.SourceRevision = 2;
        await db.SaveChangesAsync();
    }

    private static async Task<int?> InstalledRevisionAsync(
        WidgetSqliteTestDatabase database,
        Guid widgetId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        return await db
            .Widgets.IgnoreQueryFilters()
            .Where(w => w.Id == widgetId)
            .Select(w => w.InstalledSourceRevision)
            .SingleAsync();
    }

    private static async Task<string> ServedBundleAsync(
        WidgetSqliteTestDatabase database,
        GatedBuild build,
        IEventBus bus,
        Guid widgetId
    )
    {
        await using WidgetTestDbContext db = database.NewContext();
        string widgetToken = await db
            .Widgets.IgnoreQueryFilters()
            .Where(w => w.Id == widgetId)
            .Select(w => w.OverlayToken)
            .SingleAsync();
        Result<OverlayBundle> bundle = await NewService(db, build, bus)
            .GetOverlayBundleAsync(widgetToken, widgetId.ToString());
        bundle.IsSuccess.Should().BeTrue(bundle.ErrorMessage);
        return bundle.Value.Content;
    }

    /// <summary>The build tool stand-in: the bundle names its source; "V2" can be held, failed or made to throw.</summary>
    private sealed class GatedBuild
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private bool _held;
        private bool _alertsFails;
        private bool _recapThrows;
        private bool _everyV2Breaks;
        private int _v2Calls;

        public int V2Calls => _v2Calls;

        public TaskCompletionSource EnteredV2 { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IWidgetBuildService Tool { get; }

        public GatedBuild()
        {
            Tool = Substitute.For<IWidgetBuildService>();
            Tool.BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
                .Returns(call => BuildAsync(call.Arg<WidgetBuildInput>()));
        }

        public void HoldV2() => _held = true;

        public void ReleaseV2() => _gate.TrySetResult();

        public void FailV2(bool alertsFails, bool recapThrows)
        {
            _alertsFails = alertsFails;
            _recapThrows = recapThrows;
        }

        // Every V2 build breaks, so the same-pass retry breaks too: odd calls fail, even calls throw.
        public void BreakEveryV2() => _everyV2Breaks = true;

        public void FixV2() => _everyV2Breaks = false;

        private async Task<Result<WidgetBuildOutput>> BuildAsync(WidgetBuildInput input)
        {
            string source = input.Files[input.Manifest.Entry];
            if (source != "V2")
                return Result.Success(
                    new WidgetBuildOutput("compiled::" + source, new string('a', 64), "")
                );

            int call = Interlocked.Increment(ref _v2Calls);
            EnteredV2.TrySetResult();
            if (_held)
                await _gate.Task;
            if (_everyV2Breaks)
                return call % 2 == 1
                    ? Result.Failure<WidgetBuildOutput>("esbuild exploded", "WIDGET_BUILD_FAILED")
                    : throw new InvalidOperationException("esbuild missing");
            if (_alertsFails && call == 1)
                return Result.Failure<WidgetBuildOutput>("esbuild exploded", "WIDGET_BUILD_FAILED");
            if (_recapThrows && call == 2)
                throw new InvalidOperationException("esbuild missing");
            return Result.Success(new WidgetBuildOutput("compiled::V2", new string('b', 64), ""));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
