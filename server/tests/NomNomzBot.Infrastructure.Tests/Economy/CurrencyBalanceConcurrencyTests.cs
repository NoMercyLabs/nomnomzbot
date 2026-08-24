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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves S004: <c>CurrencyAccountService.AppendAsync</c> and <c>SavingsJarService</c>'s
/// contribute/withdraw no longer read-modify-write a stale in-memory balance. Every task below opens its
/// OWN <see cref="SqliteConnection"/> against a shared FILE-backed database (not the single shared
/// in-memory connection the other Economy tests use), so concurrent operations are genuinely serialized
/// by SQLite's own locking rather than by C# awaiting one connection — the same interleaving a
/// read-modify-write race depends on to lose an update.
/// </summary>
[Collection("SqliteFileConcurrency")]
public sealed class CurrencyBalanceConcurrencyTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-0000000000f3");
    private static readonly Guid Contributor = Guid.Parse("0192a000-0000-7000-8000-0000000000f4");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"nomnomz_econ_race_{Guid.NewGuid():N}.db"
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

    private EventStoreTestDbContext NewContext()
    {
        DbContextOptions<EventStoreTestDbContext> options =
            new DbContextOptionsBuilder<EventStoreTestDbContext>()
                .UseSqlite(ConnectionString)
                .Options;
        return new(options);
    }

    private CurrencyAccountService NewAccountService(EventStoreTestDbContext db)
    {
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new(db, allocator, uow, new NoOpEventBus(), Clock);
    }

    [Fact]
    public async Task Concurrent_debits_against_a_balance_covering_only_one_leave_exactly_one_winner()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Channel,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 0,
                }
            );
            seed.CurrencyAccounts.Add(
                new()
                {
                    BroadcasterId = Channel,
                    ViewerUserId = Viewer,
                    ViewerTwitchUserId = string.Empty,
                    Balance = 100,
                }
            );
            await seed.SaveChangesAsync();
        }

        const int concurrency = 10;
        const long debitAmount = 100; // only enough balance for exactly one of these to succeed

        Task<Result<CurrencyLedgerEntryDto>>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        CurrencyAccountService sut = NewAccountService(db);
                        return await sut.PostLedgerEntryAsync(
                            Channel,
                            new(
                                Viewer,
                                -debitAmount,
                                "SpendCatalog",
                                null,
                                null,
                                null,
                                null,
                                null,
                                null
                            )
                        );
                    })
                ),
        ];

        Result<CurrencyLedgerEntryDto>[] results = await Task.WhenAll(tasks);

        results
            .Count(r => r.IsSuccess)
            .Should()
            .Be(1, "only one debit can be covered by the balance");
        results
            .Where(r => r.IsFailure)
            .Select(r => r.ErrorCode)
            .Should()
            .OnlyContain(code => code == "INSUFFICIENT_FUNDS");

        using EventStoreTestDbContext verify = NewContext();
        long finalBalance = await verify
            .CurrencyAccounts.Where(a => a.BroadcasterId == Channel && a.ViewerUserId == Viewer)
            .Select(a => a.Balance)
            .FirstAsync();
        finalBalance
            .Should()
            .Be(0, "the winning debit consumed the whole balance and no lost update went negative");
        finalBalance.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Concurrent_credits_never_lose_an_update()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Channel,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 0,
                }
            );
            seed.CurrencyAccounts.Add(
                new()
                {
                    BroadcasterId = Channel,
                    ViewerUserId = Viewer,
                    ViewerTwitchUserId = string.Empty,
                    Balance = 0,
                }
            );
            await seed.SaveChangesAsync();
        }

        const int concurrency = 12;
        const long creditAmount = 10;

        Task<Result<CurrencyLedgerEntryDto>>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(_ =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        CurrencyAccountService sut = NewAccountService(db);
                        return await sut.PostLedgerEntryAsync(
                            Channel,
                            new(
                                Viewer,
                                creditAmount,
                                "EarnChat",
                                null,
                                null,
                                null,
                                null,
                                null,
                                null
                            )
                        );
                    })
                ),
        ];

        Result<CurrencyLedgerEntryDto>[] results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.IsSuccess, "no credit should be rejected");

        using EventStoreTestDbContext verify = NewContext();
        long finalBalance = await verify
            .CurrencyAccounts.Where(a => a.BroadcasterId == Channel && a.ViewerUserId == Viewer)
            .Select(a => a.Balance)
            .FirstAsync();
        finalBalance
            .Should()
            .Be(concurrency * creditAmount, "every concurrent credit must land — no lost update");
    }

    [Fact]
    public async Task Concurrent_jar_withdrawals_against_a_balance_covering_only_one_leave_exactly_one_winner()
    {
        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        Guid jarId;
        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Owner,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 0,
                }
            );
            NomNomzBot.Domain.Economy.Entities.SavingsJar jar = new()
            {
                OwnerBroadcasterId = Owner,
                Name = "Race Jar",
                Balance = 100, // only enough for exactly one 100-point withdrawal
                IsOpen = true,
            };
            seed.SavingsJars.Add(jar);
            seed.SavingsJarMemberships.Add(
                new()
                {
                    JarId = jar.Id,
                    MemberBroadcasterId = Owner,
                    Role = NomNomzBot.Domain.Economy.Enums.JarRole.Owner,
                    Status = NomNomzBot.Domain.Economy.Enums.JarMembershipStatus.Accepted,
                    AcceptedAt = Clock.GetUtcNow().UtcDateTime,
                }
            );
            await seed.SaveChangesAsync();
            jarId = jar.Id;
        }

        const int concurrency = 10;
        const long withdrawAmount = 100;

        Task<Result<JarMovementDto>>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        CurrencyAccountService accounts = NewAccountService(db);
                        SavingsJarService sut = new(db, accounts, new NoOpEventBus(), Clock);
                        return await sut.WithdrawAsync(
                            Owner,
                            new(
                                jarId,
                                Guid.Parse($"0192a000-0000-7000-8000-0000000{i:D5}"),
                                withdrawAmount,
                                Owner
                            )
                        );
                    })
                ),
        ];

        Result<JarMovementDto>[] results = await Task.WhenAll(tasks);

        results
            .Count(r => r.IsSuccess)
            .Should()
            .Be(1, "only one withdrawal can be covered by the jar balance");

        using EventStoreTestDbContext verify = NewContext();
        long finalBalance = await verify
            .SavingsJars.Where(j => j.Id == jarId)
            .Select(j => j.Balance)
            .FirstAsync();
        finalBalance.Should().Be(0);
        finalBalance.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Concurrent_jar_contributions_never_lose_an_update()
    {
        const int concurrency = 12;
        const long contributeAmount = 10;

        using (EventStoreTestDbContext schema = NewContext())
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        Guid jarId;
        using (EventStoreTestDbContext seed = NewContext())
        {
            seed.CurrencyConfigs.Add(
                new()
                {
                    BroadcasterId = Owner,
                    CurrencyName = "points",
                    IsEnabled = true,
                    StartingBalance = 1000,
                }
            );
            NomNomzBot.Domain.Economy.Entities.SavingsJar jar = new()
            {
                OwnerBroadcasterId = Owner,
                Name = "Contribution Jar",
                Balance = 0,
                IsOpen = true,
            };
            seed.SavingsJars.Add(jar);
            seed.SavingsJarMemberships.Add(
                new()
                {
                    JarId = jar.Id,
                    MemberBroadcasterId = Owner,
                    Role = NomNomzBot.Domain.Economy.Enums.JarRole.Owner,
                    Status = NomNomzBot.Domain.Economy.Enums.JarMembershipStatus.Accepted,
                    AcceptedAt = Clock.GetUtcNow().UtcDateTime,
                }
            );
            for (int i = 0; i < concurrency; i++)
                seed.CurrencyAccounts.Add(
                    new()
                    {
                        BroadcasterId = Owner,
                        ViewerUserId = Guid.Parse($"0192a000-0000-7000-8000-0000000{i:D5}"),
                        ViewerTwitchUserId = string.Empty,
                        Balance = 10_000, // plenty to cover this contributor's own contribution
                    }
                );
            await seed.SaveChangesAsync();
            jarId = jar.Id;
        }

        // Distinct contributors — many different viewers contributing to one jar, which is the real usage
        // shape and also isolates the race under test to the jar's own Balance column (each contributor's
        // own CurrencyAccount row has no contention from the others).
        Task<Result<JarMovementDto>>[] tasks =
        [
            .. Enumerable
                .Range(0, concurrency)
                .Select(i =>
                    Task.Run(async () =>
                    {
                        await using EventStoreTestDbContext db = NewContext();
                        CurrencyAccountService accounts = NewAccountService(db);
                        SavingsJarService sut = new(db, accounts, new NoOpEventBus(), Clock);
                        return await sut.ContributeAsync(
                            Owner,
                            new(
                                jarId,
                                Guid.Parse($"0192a000-0000-7000-8000-0000000{i:D5}"),
                                contributeAmount
                            )
                        );
                    })
                ),
        ];

        Result<JarMovementDto>[] results = await Task.WhenAll(tasks);

        results.Should().OnlyContain(r => r.IsSuccess, "no contribution should be rejected");

        using EventStoreTestDbContext verify = NewContext();
        long finalBalance = await verify
            .SavingsJars.Where(j => j.Id == jarId)
            .Select(j => j.Balance)
            .FirstAsync();
        finalBalance
            .Should()
            .Be(
                concurrency * contributeAmount,
                "every concurrent contribution must land — no lost update"
            );
    }

    private sealed class NoOpEventBus : NomNomzBot.Domain.Platform.Interfaces.IEventBus
    {
        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, NomNomzBot.Domain.Platform.IDomainEvent => Task.CompletedTask;

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, NomNomzBot.Domain.Platform.IDomainEvent { }
    }
}
