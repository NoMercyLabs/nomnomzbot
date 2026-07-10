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
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Regression guard for the dashboard-500 bug: <c>GET /api/v1/channels</c> grants a caller their Moderator role
/// on channels they MODERATE (a tenant other than the request's resolved own-channel tenant). Under the production
/// composing tenant + soft-delete global filter, a tenant-scoped read is BLIND to those cross-tenant memberships,
/// so the grant re-inserted the row every load — a hard 23505 against the partial unique index. The fix's lookups
/// use <c>IgnoreQueryFilters()</c> while re-applying <c>DeletedAt IS NULL</c> by hand. This proves all three facts
/// the fix relies on, against the SAME filter production uses (<see cref="ModelBuilderExtensions.ApplyTenantAndSoftDeleteFilters"/>).
/// </summary>
public sealed class CrossTenantMembershipFilterTests
{
    private static readonly Guid TenantA = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-00000000b002");
    private static readonly Guid User = Guid.Parse("0192a000-0000-7000-8000-00000000c003");
    private static readonly DateTime When = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    private static MembershipFilterContext NewDb()
    {
        MembershipFilterContext db = new(
            new DbContextOptionsBuilder<MembershipFilterContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        return db;
    }

    private static async Task SeedMembershipOnChannelBAsync(
        MembershipFilterContext db,
        DateTime? deletedAt
    )
    {
        db.Tenant = null; // seed outside any tenant scope
        db.ChannelMemberships.Add(
            new ChannelMembership
            {
                BroadcasterId = ChannelB,
                UserId = User,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = ManagementRole.Moderator.ToLevel(),
                Source = MembershipSource.TwitchBadge,
                GrantedAt = When,
                DeletedAt = deletedAt,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Tenant_scoped_read_is_blind_to_a_membership_on_another_channel()
    {
        // The root cause: acting as Tenant A, the pre-check the grant used cannot see the Channel B membership.
        MembershipFilterContext db = NewDb();
        await SeedMembershipOnChannelBAsync(db, deletedAt: null);

        db.Tenant = TenantA;
        bool seen = await db.ChannelMemberships.AnyAsync(m =>
            m.UserId == User && m.BroadcasterId == ChannelB && m.DeletedAt == null
        );

        seen.Should()
            .BeFalse("the global tenant filter hides the cross-tenant row → the blind re-insert");
    }

    [Fact]
    public async Task IgnoreQueryFilters_finds_the_cross_tenant_active_membership()
    {
        // The fix: the same lookup with IgnoreQueryFilters sees the existing row, so the grant skips/adopts it.
        MembershipFilterContext db = NewDb();
        await SeedMembershipOnChannelBAsync(db, deletedAt: null);

        db.Tenant = TenantA;
        ChannelMembership? found = await db
            .ChannelMemberships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m =>
                m.BroadcasterId == ChannelB && m.UserId == User && m.DeletedAt == null
            );

        found.Should().NotBeNull();
        found!.ManagementRole.Should().Be(ManagementRole.Moderator);
    }

    [Fact]
    public async Task IgnoreQueryFilters_still_excludes_a_soft_deleted_membership()
    {
        // The hand-applied DeletedAt IS NULL keeps soft-delete semantics — matching the partial unique index the
        // fix aligns with, so a revoked (soft-deleted) row is not mistaken for an active one.
        MembershipFilterContext db = NewDb();
        await SeedMembershipOnChannelBAsync(db, deletedAt: When);

        db.Tenant = TenantA;
        ChannelMembership? found = await db
            .ChannelMemberships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m =>
                m.BroadcasterId == ChannelB && m.UserId == User && m.DeletedAt == null
            );

        found.Should().BeNull();
    }

    /// <summary>Focused context over <see cref="ChannelMembership"/> with the SAME composing global filter prod uses.</summary>
    private sealed class MembershipFilterContext : DbContext
    {
        public Guid? Tenant;

        public MembershipFilterContext(DbContextOptions<MembershipFilterContext> options)
            : base(options) { }

        public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<ChannelMembership>().HasKey(e => e.Id);
            b.ApplyTenantAndSoftDeleteFilters(() => Tenant);
        }
    }
}
