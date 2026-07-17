// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Content.Identity;

/// <summary>
/// Seeds the global Plane-C IAM catalog (roles-permissions.md §C): the <c>IamPermissions</c> (C.1) the
/// <c>[Authorize(Policy = "&lt;key&gt;")]</c> gates resolve against, plus the system <c>IamRoles</c> (C.2)
/// and their <c>IamRolePermissions</c> bundles (C.3). GLOBAL reference data — safe on self-host, where it
/// stays inert: <c>PlatformIamService</c> keys everything on <c>IamPrincipals</c> existing, and this seeder
/// creates none. Idempotent: upserts by the natural keys (<c>Key</c> / <c>Name</c> / the join pair).
/// </summary>
public sealed class IamCatalogSeeder : ISeeder
{
    private readonly IApplicationDbContext _db;

    public IamCatalogSeeder(IApplicationDbContext db) => _db = db;

    public int Order => 6;

    /// <summary>C.1 rows: key → (category, sensitive) exactly as roles-permissions.md §C.1 tables them.</summary>
    private static readonly IReadOnlyList<(
        string Key,
        IamCategory Category,
        bool IsSensitive
    )> Permissions =
    [
        (IamPermissionKeys.TenantRead, IamCategory.Tenant, false),
        (IamPermissionKeys.TenantAccess, IamCategory.Tenant, true),
        (IamPermissionKeys.TenantSuspend, IamCategory.Tenant, true),
        (IamPermissionKeys.IamManage, IamCategory.Iam, true),
        (IamPermissionKeys.IamPrincipalCreate, IamCategory.Iam, true),
        (IamPermissionKeys.AuditRead, IamCategory.Audit, false),
        (IamPermissionKeys.FeatureFlagWrite, IamCategory.FeatureFlag, true),
        (IamPermissionKeys.BillingRead, IamCategory.Billing, false),
        (IamPermissionKeys.BillingRefund, IamCategory.Billing, true),
        (IamPermissionKeys.PlatformAnalyticsRead, IamCategory.Tenant, false),
        // Gallery review approves community code that renders on streamers' overlays — sensitive.
        (IamPermissionKeys.GalleryReview, IamCategory.Iam, true),
        // IPC dev-mode keys open a tokenless local control surface — sensitive, owner-only.
        (IamPermissionKeys.SystemIpcManage, IamCategory.FeatureFlag, true),
    ];

    /// <summary>C.2 + C.3 rows: system role → its bundled permission keys, verbatim from §C.2.</summary>
    private static readonly IReadOnlyList<(string Role, string[] Keys)> Roles =
    [
        (
            "platform-super-admin",
            [
                IamPermissionKeys.TenantRead,
                IamPermissionKeys.TenantAccess,
                IamPermissionKeys.TenantSuspend,
                IamPermissionKeys.IamManage,
                IamPermissionKeys.IamPrincipalCreate,
                IamPermissionKeys.AuditRead,
                IamPermissionKeys.FeatureFlagWrite,
                IamPermissionKeys.BillingRead,
                IamPermissionKeys.BillingRefund,
                IamPermissionKeys.PlatformAnalyticsRead,
                IamPermissionKeys.GalleryReview,
                IamPermissionKeys.SystemIpcManage,
            ]
        ),
        (
            "platform-support",
            [
                IamPermissionKeys.TenantRead,
                IamPermissionKeys.TenantAccess,
                IamPermissionKeys.AuditRead,
                IamPermissionKeys.PlatformAnalyticsRead,
            ]
        ),
        (
            "platform-trust-safety",
            [
                IamPermissionKeys.TenantRead,
                IamPermissionKeys.TenantSuspend,
                IamPermissionKeys.TenantAccess,
                IamPermissionKeys.AuditRead,
                IamPermissionKeys.GalleryReview,
            ]
        ),
        ("platform-billing", [IamPermissionKeys.BillingRead, IamPermissionKeys.BillingRefund]),
        (
            "platform-iam-admin",
            [
                IamPermissionKeys.IamManage,
                IamPermissionKeys.IamPrincipalCreate,
                IamPermissionKeys.AuditRead,
            ]
        ),
        (
            "platform-analyst",
            [IamPermissionKeys.TenantRead, IamPermissionKeys.PlatformAnalyticsRead]
        ),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        Dictionary<string, IamPermission> permissionsByKey =
            await _db.IamPermissions.ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, ct);
        foreach ((string key, IamCategory category, bool sensitive) in Permissions)
        {
            if (permissionsByKey.TryGetValue(key, out IamPermission? existing))
            {
                // The catalog is authoritative — re-sync category/sensitivity on existing installs.
                existing.Category = category;
                existing.IsSensitive = sensitive;
            }
            else
            {
                IamPermission created = new()
                {
                    Key = key,
                    Category = category,
                    IsSensitive = sensitive,
                };
                _db.IamPermissions.Add(created);
                permissionsByKey[key] = created;
            }
        }

        Dictionary<string, IamRole> rolesByName = await _db.IamRoles.ToDictionaryAsync(
            r => r.Name,
            StringComparer.Ordinal,
            ct
        );
        List<IamRolePermission> existingJoins = await _db.IamRolePermissions.ToListAsync(ct);

        foreach ((string roleName, string[] keys) in Roles)
        {
            if (!rolesByName.TryGetValue(roleName, out IamRole? role))
            {
                role = new IamRole { Name = roleName, IsSystem = true };
                _db.IamRoles.Add(role);
                rolesByName[roleName] = role;
            }
            role.IsSystem = true;

            foreach (string key in keys)
            {
                IamPermission permission = permissionsByKey[key];
                bool joined = existingJoins.Any(j =>
                    j.RoleId == role.Id && j.PermissionId == permission.Id
                );
                if (!joined)
                    _db.IamRolePermissions.Add(
                        new IamRolePermission { RoleId = role.Id, PermissionId = permission.Id }
                    );
            }
        }
    }
}
