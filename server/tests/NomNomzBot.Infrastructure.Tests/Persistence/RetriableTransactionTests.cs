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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Persistence;

/// <summary>
/// The behaviour every converted call site now depends on. Runs against a real (in-memory SQLite)
/// database with a REAL retrying execution strategy installed, because the production failure mode —
/// an attempt that gets retried — cannot be reproduced with SQLite's default no-retry strategy, and is
/// exactly what the ten converted services were rewritten to survive. Every fixture is in-process: a
/// SQLite connection this test owns, no server, no network.
/// </summary>
public sealed class RetriableTransactionTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public RetriableTransactionTests()
    {
        _connection = new("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task A_successful_operation_commits_its_writes()
    {
        await using TestContext db = NewContext();

        await db.ExecuteInTransactionAsync(async token =>
        {
            db.Rows.Add(Row("committed"));
            await db.SaveChangesAsync(token);
        });

        await using TestContext reader = NewContext();
        reader.Rows.Select(r => r.TrackName).Should().Equal("committed");
    }

    [Fact]
    public async Task An_exception_rolls_the_whole_operation_back()
    {
        await using TestContext db = NewContext();

        Func<Task> act = () =>
            db.ExecuteInTransactionAsync(async token =>
            {
                db.Rows.Add(Row("doomed"));
                await db.SaveChangesAsync(token);
                throw new InvalidOperationException("boom");
            });

        await act.Should().ThrowAsync<InvalidOperationException>();

        await using TestContext reader = NewContext();
        reader.Rows.Should().BeEmpty("the throwing attempt must leave nothing behind");
    }

    [Fact]
    public async Task A_failed_result_rolls_back_even_though_nothing_threw()
    {
        await using TestContext db = NewContext();

        // The shape every Result-returning service uses: the operation returns normally, so without
        // shouldCommit the transaction would COMMIT the writes of a business failure.
        string outcome = await db.ExecuteInTransactionAsync(
            async token =>
            {
                db.Rows.Add(Row("refused"));
                await db.SaveChangesAsync(token);
                return "failure";
            },
            shouldCommit: result => result == "success"
        );

        outcome.Should().Be("failure", "the caller still gets its own result back");
        await using TestContext reader = NewContext();
        reader.Rows.Should().BeEmpty("a failed result must undo the attempt's writes");
    }

    [Fact]
    public async Task A_retried_attempt_runs_again_and_commits_exactly_one_set_of_rows()
    {
        await using TestContext db = NewContext(retryOnce: true);
        int attempts = 0;

        await db.ExecuteInTransactionAsync(async token =>
        {
            attempts++;
            db.Rows.Add(Row($"attempt-{attempts}"));
            await db.SaveChangesAsync(token);
            if (attempts == 1)
                throw new RetryMeException();
        });

        attempts
            .Should()
            .Be(2, "the execution strategy retries the whole unit, operation included");

        await using TestContext reader = NewContext();
        // The distinction that matters: the FIRST attempt's row is gone, not merely "one row exists".
        // A commit-per-attempt bug would leave both, and a count-only check on the wrong branch could
        // still read as healthy — so assert which row survived.
        reader.Rows.Select(r => r.TrackName).Should().Equal("attempt-2");
    }

    private static SongRequestQueueItem Row(string trackName) =>
        new()
        {
            BroadcasterId = "channel-1",
            Sequence = 0,
            OwnerKey = "viewer",
            TrackUri = $"spotify:track:{trackName}",
            TrackName = trackName,
            Artist = "Artist",
            DurationMs = 1000,
            CreatedAt = DateTime.UtcNow,
        };

    private TestContext NewContext(bool retryOnce = false)
    {
        DbContextOptionsBuilder<TestContext> options = new DbContextOptionsBuilder<TestContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        if (retryOnce)
            options.UseSqlite(
                _connection,
                sqlite =>
                    sqlite.ExecutionStrategy(dependencies => new RetryOnceStrategy(dependencies))
            );

        TestContext context = new(options.Options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>Minimal context over one real table — enough to prove rows survive or vanish.</summary>
    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<SongRequestQueueItem> Rows => Set<SongRequestQueueItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<SongRequestQueueItem>().HasKey(r => r.Id);
    }

    /// <summary>Retries <see cref="RetryMeException"/> exactly once — the smallest faithful stand-in for
    /// NpgsqlRetryingExecutionStrategy, whose retry behaviour is the reason this mechanism exists.</summary>
    private sealed class RetryOnceStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is RetryMeException;
    }

    private sealed class RetryMeException : Exception;
}
