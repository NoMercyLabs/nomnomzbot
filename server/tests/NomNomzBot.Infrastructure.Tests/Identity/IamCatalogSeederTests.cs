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
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Content.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the Plane-C IAM catalog seed (roles-permissions.md §C): every §C.1 permission lands with its
/// specced category/sensitivity, the six §C.2 system roles land with their §C.3 bundles (super-admin = ALL),
/// re-running adds nothing, and — the self-host safety property — the seeder creates NO principals, so
/// <c>PlatformIamService</c> still short-circuits to owner-is-full after seeding.
/// </summary>
public sealed class IamCatalogSeederTests
{
    private static async Task<int> SeedAsync(AuthDbContext db)
    {
        await new IamCatalogSeeder(db).SeedAsync();
        return await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seeds_every_c1_permission_with_its_specced_category_and_sensitivity()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        List<IamPermission> all = await db.IamPermissions.ToListAsync();
        all.Select(p => p.Key)
            .Should()
            .BeEquivalentTo(IamPermissionKeys.All, "the seed IS the closed §C.1 catalog");

        IamPermission tenantAccess = all.Single(p => p.Key == IamPermissionKeys.TenantAccess);
        tenantAccess.Category.Should().Be(IamCategory.Tenant);
        tenantAccess.IsSensitive.Should().BeTrue();

        IamPermission auditRead = all.Single(p => p.Key == IamPermissionKeys.AuditRead);
        auditRead.Category.Should().Be(IamCategory.Audit);
        auditRead.IsSensitive.Should().BeFalse();
    }

    [Fact]
    public async Task Seeds_the_six_system_roles_with_super_admin_holding_every_permission()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        List<IamRole> roles = await db.IamRoles.ToListAsync();
        roles
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo([
                "platform-super-admin",
                "platform-support",
                "platform-trust-safety",
                "platform-billing",
                "platform-iam-admin",
                "platform-analyst",
            ]);
        roles.Should().OnlyContain(r => r.IsSystem, "§C.2: all seeded roles are IsSystem");

        IamRole superAdmin = roles.Single(r => r.Name == "platform-super-admin");
        List<Guid> superAdminPermissionIds = await db
            .IamRolePermissions.Where(j => j.RoleId == superAdmin.Id)
            .Select(j => j.PermissionId)
            .ToListAsync();
        superAdminPermissionIds
            .Should()
            .HaveCount(IamPermissionKeys.All.Count, "super-admin = ALL");

        IamRole billing = roles.Single(r => r.Name == "platform-billing");
        List<string> billingKeys = await db
            .IamRolePermissions.Where(j => j.RoleId == billing.Id)
            .Join(db.IamPermissions, j => j.PermissionId, p => p.Id, (j, p) => p.Key)
            .ToListAsync();
        billingKeys
            .Should()
            .BeEquivalentTo([IamPermissionKeys.BillingRead, IamPermissionKeys.BillingRefund]);
    }

    [Fact]
    public async Task Reseeding_is_idempotent_and_adds_no_duplicates()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);
        int permissions = await db.IamPermissions.CountAsync();
        int roles = await db.IamRoles.CountAsync();
        int joins = await db.IamRolePermissions.CountAsync();

        int addedOnReseed = await SeedAsync(db);

        addedOnReseed.Should().Be(0);
        (await db.IamPermissions.CountAsync()).Should().Be(permissions);
        (await db.IamRoles.CountAsync()).Should().Be(roles);
        (await db.IamRolePermissions.CountAsync()).Should().Be(joins);
    }

    [Fact]
    public async Task Seeding_creates_no_principals_so_self_host_stays_owner_is_full()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await SeedAsync(db);

        (await db.IamPrincipals.AnyAsync())
            .Should()
            .BeFalse(
                "the catalog is inert reference data; principals flip the plane to default-deny"
            );
    }
}
