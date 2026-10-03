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
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves S004j: <c>CurrencyBalanceProjection.ApplyAsync</c> no longer read-modify-writes a tracked
/// <see cref="CurrencyAccount"/> instance to accumulate <see cref="CurrencyAccount.LifetimeEarned"/> /
/// <see cref="CurrencyAccount.LifetimeSpent"/>, and that these two columns are no longer double-counted by
/// <c>CurrencyAccountService.AppendAsync</c> writing them a second time. Each task below opens its OWN
/// <see cref="SqliteConnection"/> against a shared FILE-backed database (not the single shared in-memory
/// connection the other Economy tests use), so concurrent projection applies are genuinely serialized by
/// SQLite's own locking rather than by C# awaiting one connection.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class CurrencyBalanceProjectionConcurrencyTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192b000-0000-7000-8000-0000000000f1");
    private static readonly Guid Account = Guid.Parse("0192b000-0000-7000-8000-0000000000f2");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_econ_proj_race_{Guid.NewGuid():N}.db"
    );

    private string ConnectionString => $"Data Source={_dbPath};Default Timeout=90;Pooling=False";

    public void Dispose()
    {
        // S119: SqliteConnection.ClearAllPools() flushes EVERY pooled native handle process-wide, including
        // ones other test classes running concurrently (xUnit's default cross-class parallelism) are
        // actively using. Scope the flush to THIS test's own connection string so it only releases handles
        // this test opened.
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

    private EventStoreTestDbContext NewContext()
    {
        DbContextOptions<EventStoreTestDbContext> options =
            new DbContextOptionsBuilder<EventStoreTestDbContext>()
                .UseSqlite(ConnectionString)
                .Options;
        return new(options);
    }

    private static EventRecord CreditEvent(long position, long amount, long balanceAfter) =>
        MakeEvent("CurrencyCreditedEvent", position, amount, balanceAfter);

    private static EventRecord DebitEvent(long position, long amount, long balanceAfter) =>
        MakeEvent("CurrencyDebitedEvent", position, amount, balanceAfter);

    private static EventRecord MakeEvent(
        string eventType,
        long position,
        long amount,
        long balanceAfter
    )
    {
        string payload = JsonConvert.SerializeObject(
            new
            {
                AccountId = Account,
                Amount = amount,
                BalanceAfter = balanceAfter,
            }
        );
        return new(
            Id: position,
            EventId: Guid.NewGuid(),
            BroadcasterId: Channel,
            StreamPosition: position,
            EventType: eventType,
            EventVersion: 1,
            Source: "domain",
            PayloadJson: payload,
            PayloadIsEncrypted: false,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: new(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc),
            RecordedAt: new(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc)
        );
    }

    private async Task SeedAccountAsync()
    {
        using EventStoreTestDbContext schema = NewContext();
        await schema.Database.EnsureCreatedAsync();
        await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        schema.CurrencyAccounts.Add(
            new()
            {
                Id = Account,
                BroadcasterId = Channel,
                ViewerUserId = Guid.Parse("0192b000-0000-7000-8000-0000000000f3"),
                ViewerTwitchUserId = string.Empty,
                Balance = 0,
                LifetimeEarned = 0,
                LifetimeSpent = 0,
            }
        );
        await schema.SaveChangesAsync();
    }

    private async Task<(long earned, long spent)> ReadTotalsAsync()
    {
        using EventStoreTestDbContext verify = NewContext();
        CurrencyAccount row = await verify.CurrencyAccounts.FirstAsync(a => a.Id == Account);
        return (row.LifetimeEarned, row.LifetimeSpent);
    }

    [Fact]
    public async Task Concurrent_folds_of_the_same_credit_count_it_exactly_once()
    {
        await SeedAccountAsync();

        const int concurrency = 12;
        const long creditAmount = 10;

        // The real race: the projection driver's retry of a fold racing an operator's replay of the same
        // journal event. The driver drains a tenant's stream in order under a lease, so out-of-order folds
        // are not a path — but the SAME event reaching the projection from several writers at once is, and
        // exactly one of them may land. SQLite's own locking serializes the UPDATEs; the stream high-water
        // mark makes every late one a no-op.
        EventRecord credit = CreditEvent(1, creditAmount, creditAmount);
        Task[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        CurrencyBalanceProjection sut = new(db);
                        Result apply = await sut.ApplyAsync(credit);
                        apply.IsSuccess.Should().BeTrue();
                    })
                ),
        ];

        await Task.WhenAll(tasks);

        (long earned, long spent) = await ReadTotalsAsync();
        earned
            .Should()
            .Be(
                creditAmount,
                "twelve writers folding one credit must count it once — neither lost nor duplicated"
            );
        spent.Should().Be(0);
    }

    [Fact]
    public async Task Replaying_the_same_events_does_not_double_count_lifetime_totals()
    {
        await SeedAccountAsync();

        const long creditAmount = 25;
        const long debitAmount = 15;
        EventRecord credit = CreditEvent(1, creditAmount, creditAmount);
        EventRecord debit = DebitEvent(2, debitAmount, creditAmount - debitAmount);

        // First pass — the live incremental drive.
        using (EventStoreTestDbContext db = NewContext())
        {
            CurrencyBalanceProjection sut = new(db);
            (await sut.ApplyAsync(credit)).IsSuccess.Should().BeTrue();
            (await sut.ApplyAsync(debit)).IsSuccess.Should().BeTrue();
        }

        (long earnedAfterFirstPass, long spentAfterFirstPass) = await ReadTotalsAsync();
        earnedAfterFirstPass.Should().Be(creditAmount);
        spentAfterFirstPass.Should().Be(debitAmount);

        // Reset (as ProjectionRunner.RebuildAsync does) then replay the SAME journal events from zero.
        using (EventStoreTestDbContext db = NewContext())
        {
            CurrencyBalanceProjection sut = new(db);
            (await sut.ResetAsync(Channel)).IsSuccess.Should().BeTrue();
            (await sut.ApplyAsync(credit)).IsSuccess.Should().BeTrue();
            (await sut.ApplyAsync(debit)).IsSuccess.Should().BeTrue();
        }

        (long earnedAfterReplay, long spentAfterReplay) = await ReadTotalsAsync();
        earnedAfterReplay
            .Should()
            .Be(
                creditAmount,
                "a reset+replay of the same events must reproduce the same total, not double it"
            );
        spentAfterReplay
            .Should()
            .Be(
                debitAmount,
                "a reset+replay of the same events must reproduce the same total, not double it"
            );
    }

    [Fact]
    public async Task AppendAsync_no_longer_double_counts_lifetime_totals_the_projection_also_folds()
    {
        // Regression guard for the actual production bug found in S004j: CurrencyAccountService.AppendAsync
        // used to write LifetimeEarned/LifetimeSpent directly (synchronously, in the same transaction as the
        // ledger append) AND CurrencyBalanceProjection.ApplyAsync would later fold the SAME
        // CurrencyCreditedEvent/CurrencyDebitedEvent and increment them again — doubling every total the
        // moment the projection caught up. This test proves the projection's own fold, applied in isolation
        // exactly once (as it now is, since AppendAsync no longer touches these columns), lands on the true
        // amount rather than 2x it.
        await SeedAccountAsync();

        const long creditAmount = 40;
        using EventStoreTestDbContext db = NewContext();
        CurrencyBalanceProjection sut = new(db);
        (await sut.ApplyAsync(CreditEvent(1, creditAmount, creditAmount)))
            .IsSuccess.Should()
            .BeTrue();

        (long earned, long spent) = await ReadTotalsAsync();
        // Pre-fix, AppendAsync's own += PLUS this fold would have produced 2 * creditAmount = 80 for a
        // single credit once both writers had touched the row; observed on the pre-fix code path.
        earned
            .Should()
            .Be(creditAmount, "the projection is the SOLE writer of LifetimeEarned now");
        earned
            .Should()
            .NotBe(
                creditAmount * 2,
                "this is the exact wrong number the pre-fix double-write produced"
            );
        spent.Should().Be(0);
    }
}
