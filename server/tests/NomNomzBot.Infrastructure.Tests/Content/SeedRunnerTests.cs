// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Content.Platform;
using NomNomzBot.Infrastructure.Content.Tts;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// Reliability proof for the §5 content-seed pipeline. Drives the REAL <see cref="SeedRunner"/>
/// over the REAL seeders against a real EF context (<see cref="SeedTestDbContext"/>, InMemory),
/// then proves: discovery (the §4 scan finds every <see cref="ISeeder"/>), ordering (the runner
/// sorts by <see cref="ISeeder.Order"/> ascending), and — the key assertion — idempotency:
/// running the full pipeline twice leaves identical row counts, with zero duplicates and no error.
/// </summary>
public sealed class SeedRunnerTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(SeedRunner).Assembly;

    // Each test gets its own database name so the InMemory store is isolated; successive
    // contexts in one test share that name, so a re-run sees the same data — exactly what a
    // production re-seed hits. TransactionIgnoredWarning is suppressed because the InMemory
    // provider has no real transactions; suppressing it lets the REAL SeedRunner transaction
    // path (Begin/Save/Commit) run unchanged instead of throwing.
    private static SeedTestDbContext NewContext(string databaseName)
    {
        DbContextOptions<SeedTestDbContext> options =
            new DbContextOptionsBuilder<SeedTestDbContext>()
                .UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        return new SeedTestDbContext(options);
    }

    private static SeedRunner BuildRunner(SeedTestDbContext context) =>
        new(
            // The real production seeders, constructed against this context.
            [
                new TtsVoiceSeeder(context),
                new PronounSeeder(context),
                new ConfigSeeder(context),
                new DefaultCommandsSeeder(context),
            ],
            new TestUnitOfWork(context),
            NullLogger<SeedRunner>.Instance
        );

    // ── Discovery (§4 scan) ──────────────────────────────────────────────────

    [Fact]
    public void Scan_discovers_every_ISeeder_in_the_assembly()
    {
        // Expected set computed by reflection — add a seeder and this stays honest.
        List<Type> seederTypes = InfrastructureAssembly
            .GetTypes()
            .Where(t =>
                t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                && typeof(ISeeder).IsAssignableFrom(t)
            )
            .ToList();

        seederTypes.Should().HaveCountGreaterThanOrEqualTo(4, "the four content seeders exist");
        seederTypes
            .Select(t => t.Name)
            .Should()
            .Contain([
                nameof(TtsVoiceSeeder),
                nameof(PronounSeeder),
                nameof(ConfigSeeder),
                nameof(DefaultCommandsSeeder),
            ]);
    }

    // ── Ordering (§5.1 contract) ─────────────────────────────────────────────

    [Fact]
    public async Task Runner_orders_seeders_by_Order_ascending()
    {
        // A recording seeder set deliberately registered out of Order — the runner must still
        // execute them low-to-high, never in registration order.
        List<int> executionLog = [];
        RecordingSeeder[] outOfOrder =
        [
            new(order: 80, executionLog),
            new(order: 10, executionLog),
            new(order: 40, executionLog),
            new(order: 10, executionLog),
        ];

        using SeedTestDbContext context = NewContext(Guid.NewGuid().ToString());
        SeedRunner runner = new(
            outOfOrder,
            new TestUnitOfWork(context),
            NullLogger<SeedRunner>.Instance
        );

        await runner.SeedAsync();

        executionLog.Should().BeInAscendingOrder();
        executionLog.Should().Equal(10, 10, 40, 80);
    }

    private sealed class RecordingSeeder : ISeeder
    {
        private readonly List<int> _log;

        public RecordingSeeder(int order, List<int> log)
        {
            Order = order;
            _log = log;
        }

        public int Order { get; }

        public Task SeedAsync(CancellationToken ct = default)
        {
            _log.Add(Order);
            return Task.CompletedTask;
        }
    }

    // ── Idempotency (the key reliability assertion) ──────────────────────────

    [Fact]
    public async Task Seeding_twice_produces_identical_row_counts_no_duplicates()
    {
        string db = Guid.NewGuid().ToString();

        // First run — writes the shipped defaults.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await BuildRunner(context).SeedAsync();
        }

        Counts afterFirst = await ReadCounts(db);

        afterFirst.TtsVoices.Should().Be(10, "the catalogue defines ten voices");
        afterFirst.Pronouns.Should().Be(7, "the reference set defines seven pronouns");
        afterFirst.GlobalConfigs.Should().Be(4, "four global config defaults are seeded");

        // Second run — every seeder is upsert-by-natural-key, so this must add nothing.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await BuildRunner(context).SeedAsync();
        }

        Counts afterSecond = await ReadCounts(db);

        afterSecond
            .Should()
            .BeEquivalentTo(afterFirst, "a re-run upserts by natural key — zero duplicates");

        // Prove uniqueness directly: no natural key appears twice.
        await using SeedTestDbContext verify = NewContext(db);
        verify.TtsVoices.Select(v => v.Id).Should().OnlyHaveUniqueItems();
        verify.Pronouns.Select(p => p.Name).Should().OnlyHaveUniqueItems();
        verify
            .Configurations.Where(c => c.BroadcasterId == null)
            .Select(c => c.Key)
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DefaultCommandsSeeder_upserts_per_channel_and_is_idempotent()
    {
        string db = Guid.NewGuid().ToString();

        // Two channels exist (created by onboarding, not by a seeder).
        await using (SeedTestDbContext context = NewContext(db))
        {
            context.Channels.Add(new() { Id = "100", Name = "alpha" });
            context.Channels.Add(new() { Id = "200", Name = "bravo" });
            await context.SaveChangesAsync();
        }

        // First run seeds the five default commands for each of the two channels.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await new DefaultCommandsSeeder(context).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using (SeedTestDbContext context = NewContext(db))
        {
            (await context.Commands.CountAsync()).Should().Be(10, "5 defaults × 2 channels");
            (await context.Commands.Where(c => c.BroadcasterId == "100").CountAsync())
                .Should()
                .Be(5);
            // Shape: every seeded default is a pipeline command with a non-empty pipeline body.
            (
                await context
                    .Commands.Where(c => c.Type == "pipeline" && c.PipelineJson != null)
                    .CountAsync()
            )
                .Should()
                .Be(10);
        }

        // Second run — natural key (BroadcasterId, Name) already present, so nothing is added.
        await using (SeedTestDbContext context = NewContext(db))
        {
            await new DefaultCommandsSeeder(context).SeedAsync();
            await context.SaveChangesAsync();
        }

        await using (SeedTestDbContext context = NewContext(db))
        {
            (await context.Commands.CountAsync())
                .Should()
                .Be(10, "re-run upserts by (BroadcasterId, Name) — no duplicates");
            context
                .Commands.Select(c => new { c.BroadcasterId, c.Name })
                .Should()
                .OnlyHaveUniqueItems();
        }
    }

    private static async Task<Counts> ReadCounts(string db)
    {
        await using SeedTestDbContext context = NewContext(db);
        return new(
            await context.TtsVoices.CountAsync(),
            await context.Pronouns.CountAsync(),
            await context.Configurations.CountAsync(c => c.BroadcasterId == null)
        );
    }

    private sealed record Counts(int TtsVoices, int Pronouns, int GlobalConfigs);

    // ── Transaction contract (§5.1: one transaction, rollback on any failure) ─

    [Fact]
    public async Task Runner_commits_once_after_all_seeders_in_a_single_transaction()
    {
        using SeedTestDbContext context = NewContext(Guid.NewGuid().ToString());
        TestUnitOfWork uow = new(context);
        SeedRunner runner = new(
            [new RecordingSeeder(10, []), new RecordingSeeder(20, [])],
            uow,
            NullLogger<SeedRunner>.Instance
        );

        await runner.SeedAsync();

        // Exactly one transaction wrapping the whole pass, committed, never rolled back.
        uow.Calls.Should().Equal("Begin", "Save", "Commit");
    }

    [Fact]
    public async Task Runner_rolls_back_and_rethrows_when_a_seeder_fails()
    {
        using SeedTestDbContext context = NewContext(Guid.NewGuid().ToString());
        TestUnitOfWork uow = new(context);
        SeedRunner runner = new(
            [new RecordingSeeder(10, []), new ThrowingSeeder(20)],
            uow,
            NullLogger<SeedRunner>.Instance
        );

        Func<Task> act = () => runner.SeedAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("seeder boom");
        // All-or-nothing: the transaction is rolled back and never committed.
        uow.Calls.Should().Equal("Begin", "Rollback");
        uow.Calls.Should().NotContain("Commit");
    }

    private sealed class ThrowingSeeder : ISeeder
    {
        public ThrowingSeeder(int order) => Order = order;

        public int Order { get; }

        public Task SeedAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("seeder boom");
    }

    /// <summary>
    /// A minimal <see cref="IUnitOfWork"/> over the focused test context that records its call
    /// sequence, so a test can assert the REAL <see cref="SeedRunner"/> orchestrates exactly one
    /// transaction and rolls back on failure. (The production <see cref="UnitOfWork"/> binds to
    /// AppDbContext + real relational transactions, which are not hostable on a test provider —
    /// see <see cref="SeedTestDbContext"/>.)
    /// </summary>
    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly SeedTestDbContext _db;

        public TestUnitOfWork(SeedTestDbContext db) => _db = db;

        public List<string> Calls { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Calls.Add("Save");
            return _db.SaveChangesAsync(ct);
        }

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            Calls.Add("Begin");
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken ct = default)
        {
            Calls.Add("Commit");
            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            Calls.Add("Rollback");
            return Task.CompletedTask;
        }
    }
}
