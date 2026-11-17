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

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Builds the real write queue over a fake provider edge and a fake clock.</summary>
internal static class MusicWriteQueueTestKit
{
    internal static readonly DateTimeOffset Start = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Opens a fresh in-memory database; a second queue built over it models a restart.</summary>
    internal static SqliteConnection NewDatabase()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        using AppDbContext db = new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
        );
        db.Database.EnsureCreated();
        return connection;
    }

    internal static List<MusicHeldWrite> HeldRows(SqliteConnection database)
    {
        using AppDbContext db = new(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(database).Options
        );
        return db.MusicHeldWrites.OrderBy(r => r.CreatedAt).ToList();
    }

    internal static MusicWriteQueue Over(
        IMusicProviderManageApi provider,
        FakeTimeProvider? clock = null,
        IYouTubeQuotaCooldown? quota = null,
        SqliteConnection? database = null
    )
    {
        FakeTimeProvider time = clock ?? new(Start);
        SqliteConnection connection = database ?? NewDatabase();
        ServiceCollection services = new();
        services.AddScoped(_ => provider);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        return new(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            quota ?? new YouTubeQuotaCooldown(time),
            time,
            NullLogger<MusicWriteQueue>.Instance
        );
    }
}
