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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// S-TENANT-GUIDCASE — root-cause guard. Microsoft.Data.Sqlite binds a Guid parameter as canonical
/// uppercase-hyphenated TEXT and SQLite text comparison is ordinal, so a <c>BroadcasterId</c> row whose stored
/// text drifted to another case (a raw import, a Postgres-to-SQLite restore, a hand-written seed literal —
/// <c>Guid.ToString()</c> defaults to LOWERCASE) is invisible to the global tenant query filter
/// (<see cref="ModelBuilderExtensions.ApplyTenantAndSoftDeleteFilters"/>) and to every Guid-id
/// <c>.Contains(...)</c> predicate. The fix is a <c>NOCASE</c> collation on every Guid(?) column, applied
/// SQLite-only in <see cref="ProviderCompatibilityExtensions.ApplySqliteCompatibility"/>.
///
/// Every case below runs against a REAL relational SQLite database (not EF InMemory, which has no column
/// collation at all) through the exact production filter-building step, on <see cref="ChannelMembership"/> —
/// a real <c>ITenantScoped</c> entity, the same one <c>CrossTenantMembershipFilterTests</c> uses.
/// </summary>
public sealed class GuidNocaseCollationTenantFilterTests
{
    private static readonly Guid TenantA = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly Guid TenantB = Guid.Parse("0192a000-0000-7000-8000-00000000b002");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-00000000c003");
    private static readonly DateTime When = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Root symptom, confirmed red before the fix: this test was run against the pre-fix
    /// <c>ApplySqliteCompatibility</c> (the <c>NOCASE</c> block commented out) and failed with
    /// "Expected found not to be &lt;null&gt;, but found &lt;null&gt;." — the exact shape of the production bug:
    /// a tenant-scoped row with a lower-cased <c>BroadcasterId</c> silently disappears from a normal tenant-scoped
    /// query, because <c>e.BroadcasterId == currentBroadcasterId()</c> compares the lower-cased stored text
    /// against Microsoft.Data.Sqlite's canonical uppercase parameter and never matches. Post-fix, the same query
    /// against the same corrupted row returns it.
    /// </summary>
    [Fact]
    public async Task Tenant_scoped_query_finds_a_row_whose_BroadcasterId_is_stored_non_canonically()
    {
        GuidCaseFilterContext db = GuidCaseFilterContext.New();

        Guid membershipId = Guid.CreateVersion7();
        db.Tenant = null; // seed outside any tenant scope
        db.ChannelMemberships.Add(
            new()
            {
                Id = membershipId,
                BroadcasterId = TenantA,
                UserId = User,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = When,
            }
        );
        await db.SaveChangesAsync();

        // Simulate the real-world corruption a raw import / Postgres-to-SQLite restore leaves behind: the FK
        // text survives, only its casing drifts away from Microsoft.Data.Sqlite's canonical uppercase form.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE ChannelMemberships SET BroadcasterId = lower(BroadcasterId) WHERE Id = {0}",
            membershipId
        );

        db.Tenant = TenantA;
        ChannelMembership? found = await db.ChannelMemberships.FirstOrDefaultAsync(m =>
            m.Id == membershipId
        );

        found
            .Should()
            .NotBeNull(
                "the NOCASE collation on BroadcasterId must make the tenant filter match regardless of stored casing"
            );
        found!.UserId.Should().Be(User);
    }

    /// <summary>
    /// Tenant isolation must survive the case-insensitivity fix: NOCASE only folds letter case on IDENTICAL hex
    /// digits — two structurally different Guids must never compare equal under it. A query scoped to Tenant A
    /// must never return Tenant B's row, canonically cased or not.
    /// </summary>
    [Fact]
    public async Task Tenant_scoped_query_never_returns_a_different_tenants_row()
    {
        GuidCaseFilterContext db = GuidCaseFilterContext.New();

        Guid membershipId = Guid.CreateVersion7();
        db.Tenant = null;
        db.ChannelMemberships.Add(
            new()
            {
                Id = membershipId,
                BroadcasterId = TenantB,
                UserId = User,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = When,
            }
        );
        await db.SaveChangesAsync();

        // Corrupt Tenant B's row casing too — isolation must hold even when both sides carry odd casing.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE ChannelMemberships SET BroadcasterId = lower(BroadcasterId) WHERE Id = {0}",
            membershipId
        );

        db.Tenant = TenantA;
        List<ChannelMembership> seenByA = await db.ChannelMemberships.ToListAsync();

        seenByA
            .Should()
            .BeEmpty(
                "a query scoped to Tenant A must never return Tenant B's row — case-insensitivity must not become id-fuzziness"
            );
    }

    /// <summary>
    /// The mechanism must be a genuine no-op on Postgres: the model built with <c>UseNpgsql</c> (no live
    /// connection needed — this only inspects the built model, exactly like <c>PendingModelChangesGuardTests</c>)
    /// must carry NO collation on any Guid column, since Postgres has a native <c>uuid</c> type with no text
    /// casing to normalize and no <c>NOCASE</c> collating sequence at all.
    /// </summary>
    [Fact]
    public void Npgsql_model_carries_no_collation_on_any_Guid_column()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=nomnomzbot;Username=postgres;Password=postgres",
                npgsqlOptions =>
                    npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)
            )
            .Options;
        using AppDbContext context = new(options);
        IModel designTimeModel = context.GetService<IDesignTimeModel>().Model;

        List<IProperty> guidProperties = designTimeModel
            .GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))
            .ToList();

        guidProperties.Should().NotBeEmpty("the model has plenty of Guid columns to check");
        guidProperties
            .Should()
            .OnlyContain(
                p => p.GetCollation() == null,
                "NOCASE is a SQLite-only fix — the Postgres model must be byte-for-byte unaffected"
            );
    }

    /// <summary>
    /// The mirror check: the SQLite model DOES carry the fix, on every Guid(?) column — proving the mechanism is
    /// provider-conditional (present on SQLite, absent on Postgres above), not accidentally universal.
    /// </summary>
    [Fact]
    public void Sqlite_model_carries_NOCASE_collation_on_every_Guid_column()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(
                "Data Source=:memory:",
                sqliteOptions => sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite")
            )
            .Options;
        using AppDbContext context = new(options);
        IModel designTimeModel = context.GetService<IDesignTimeModel>().Model;

        List<IProperty> guidProperties = designTimeModel
            .GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))
            .ToList();

        guidProperties.Should().NotBeEmpty("the model has plenty of Guid columns to check");
        guidProperties
            .Should()
            .OnlyContain(
                p => p.GetCollation() == "NOCASE",
                "every Guid(?) column on SQLite must carry the NOCASE collation, not just BroadcasterId"
            );
    }

    /// <summary>Focused context over <see cref="ChannelMembership"/> with the SAME composing global filter prod uses.</summary>
    private sealed class GuidCaseFilterContext : DbContext
    {
        public Guid? Tenant;

        // A REAL relational SQLite database (S-API-TESTS-INMEMORY): column collation only exists on a
        // relational provider — EF InMemory has no concept of it and could never reproduce this bug or its fix.
        private readonly SqliteConnection _connection;

        private GuidCaseFilterContext(
            DbContextOptions<GuidCaseFilterContext> options,
            SqliteConnection connection
        )
            : base(options) => _connection = connection;

        public static GuidCaseFilterContext New()
        {
            SqliteConnection connection = new("Data Source=:memory:");
            connection.Open();
            GuidCaseFilterContext db = new(
                new DbContextOptionsBuilder<GuidCaseFilterContext>().UseSqlite(connection).Options,
                connection
            );
            db.Database.EnsureCreated();
            return db;
        }

        public override void Dispose()
        {
            base.Dispose();
            _connection.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _connection.DisposeAsync();
        }

        public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<ChannelMembership>().HasKey(e => e.Id);
            b.ApplyTenantAndSoftDeleteFilters(() => Tenant);
            // The exact production step under test — applies the NOCASE collation to every Guid(?) column.
            b.ApplySqliteCompatibility();
        }
    }
}
