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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves S004b: <c>WidgetService.InstallFromGalleryAsync</c> no longer loses updates to the shared
/// <c>WidgetGalleryItem.InstallCount</c> under an unguarded in-memory <c>item.InstallCount += 1</c> +
/// SaveChanges. Every concurrent install is a DIFFERENT channel installing the SAME gallery item — the real
/// usage shape — each opening its OWN <see cref="SqliteConnection"/> against a shared FILE-backed database
/// (like <c>CurrencyBalanceConcurrencyTests</c>/<c>CatalogStockConcurrencyTests</c>, and unlike
/// <c>WidgetSqliteTestDatabase</c>'s single kept-open <c>:memory:</c> connection), so the race is genuinely
/// arbitrated by SQLite's own locking rather than by C# awaiting one connection.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class WidgetGalleryInstallConcurrencyTests : IDisposable
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_widget_install_race_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90";

    public void Dispose()
    {
        // S119: SqliteConnection.ClearAllPools() flushes EVERY pooled native handle process-wide, including
        // ones other test classes running concurrently (xUnit's default cross-class parallelism) are
        // actively using — a documented source of a native e_sqlite3 crash with no managed exception and no
        // dump. Scope the flush to THIS test's own connection string so it only releases handles this test
        // opened.
        using (SqliteConnection ownPool = new(ConnectionString))
            SqliteConnection.ClearPool(ownPool);

        foreach (
            string path in new[]
            {
                _dbPath,
                $"{_dbPath}-wal",
                $"{_dbPath}-shm",
                $"{_dbPath}-journal",
            }
        )
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private WidgetTestDbContext NewContext()
    {
        DbContextOptions<WidgetTestDbContext> options =
            new DbContextOptionsBuilder<WidgetTestDbContext>()
                .UseSqlite(ConnectionString)
                .AddInterceptors(
                    new SoftDeleteInterceptor(
                        Clock,
                        new NomNomzBot.Infrastructure.Tests.Platform.Persistence.NullCurrentUserService()
                    )
                )
                .Options;
        return new(options);
    }

    private static WidgetService NewWidgetService(WidgetTestDbContext db)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IEventBus eventBus = Substitute.For<IEventBus>();
        IWidgetBuildService buildService = Substitute.For<IWidgetBuildService>();
        buildService
            .BuildAsync(Arg.Any<WidgetBuildInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new WidgetBuildOutput("compiled-bundle", new('a', 64), "ok")));
        IWidgetSettingsSchemaProvider settingsSchemas =
            Substitute.For<IWidgetSettingsSchemaProvider>();
        IMusicService musicService = Substitute.For<IMusicService>();
        IScriptStorageService scriptStorage = Substitute.For<IScriptStorageService>();

        return new(
            db,
            configuration,
            eventBus,
            buildService,
            settingsSchemas,
            Clock,
            musicService,
            scriptStorage,
            new PipelineStepReferenceScanner(db),
            Substitute.For<IOverlayPresenceRegistry>()
        );
    }

    [Fact]
    public async Task Concurrent_installs_of_one_gallery_item_increment_InstallCount_exactly_once_each()
    {
        using (WidgetTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        const int concurrency = 12;
        Guid galleryItemId;
        Guid[] channelIds = new Guid[concurrency];

        using (WidgetTestDbContext seed = NewContext())
        {
            WidgetGalleryItem galleryItem = new()
            {
                Name = "Alert Box",
                Framework = "vanilla",
                TrustTier = "first_party",
                SourceKind = "in_repo",
                NaturalKey = "alerts",
                SourceCode = "console.log('widget');",
                ReviewStatus = "verified",
                InstallCount = 0,
            };
            seed.WidgetGalleryItems.Add(galleryItem);
            galleryItemId = galleryItem.Id;

            for (int i = 0; i < concurrency; i++)
            {
                Channel channel = new()
                {
                    OwnerUserId = Guid.CreateVersion7(),
                    Provider = "twitch",
                    ExternalChannelId = $"ext-{i}",
                    Name = $"channel{i}",
                    NameNormalized = $"channel{i}",
                    OverlayToken = $"overlay-token-{i:D4}",
                };
                seed.Channels.Add(channel);
                channelIds[i] = channel.Id;
            }
            await seed.SaveChangesAsync();
        }

        Task<Result<WidgetDetail>>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        await using WidgetTestDbContext db = NewContext();
                        WidgetService sut = NewWidgetService(db);
                        return await sut.InstallFromGalleryAsync(
                            channelIds[i].ToString(),
                            galleryItemId.ToString()
                        );
                    })
                ),
        ];

        Result<WidgetDetail>[] results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.IsSuccess, "every channel's install should succeed");

        using WidgetTestDbContext verify = NewContext();
        int finalInstallCount = await verify
            .WidgetGalleryItems.Where(i => i.Id == galleryItemId)
            .Select(i => i.InstallCount)
            .FirstAsync();
        finalInstallCount
            .Should()
            .Be(concurrency, "every concurrent install must land — no lost update");
    }
}
