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
        (IamPermissionKeys.BillingWrite, IamCategory.Billing, true),
        (IamPermissionKeys.BillingRefund, IamCategory.Billing, true),
        (IamPermissionKeys.PlatformAnalyticsRead, IamCategory.Tenant, false),
        // GDPR erasure of another subject — destructive, distinct from the tenant:access support-visit key.
        (IamPermissionKeys.ComplianceErasure, IamCategory.Tenant, true),
        // Gallery review approves community code that renders on streamers' overlays — sensitive.
        (IamPermissionKeys.GalleryReview, IamCategory.Iam, true),
        // IPC dev-mode keys open a tokenless local control surface — sensitive, owner-only.
        (IamPermissionKeys.SystemIpcManage, IamCategory.FeatureFlag, true),
        // Act-as impersonation mints a token carrying another user's full identity — sensitive.
        (IamPermissionKeys.UserImpersonate, IamCategory.Iam, true),
        // Platform content authoring/propagation (platform-admin.md §4) — new IamCategory.Content bucket.
        (IamPermissionKeys.ContentRead, IamCategory.Content, false),
        (IamPermissionKeys.ContentAuthor, IamCategory.Content, true),
        (IamPermissionKeys.ContentPublish, IamCategory.Content, true),
        // force overwrites tenant-edited copies — the most sensitive publish mode (§2.1).
        (IamPermissionKeys.ContentPublishForce, IamCategory.Content, true),
        // Per-tenant quota exceptions and lifecycle recovery/destruction (S-ADMIN-3).
        (IamPermissionKeys.TenantQuotaManage, IamCategory.Tenant, true),
        (IamPermissionKeys.TenantRemigrate, IamCategory.Tenant, true),
        // The most destructive tenant operation in the product — deliberately its own key, never bundled
        // implicitly with tenant:suspend or tenant:access.
        (IamPermissionKeys.TenantErase, IamCategory.Tenant, true),
        // Cross-tenant person lookup (S-ADMIN-7a) — reads one subject's real state in EVERY tenant, so it
        // crosses the tenant boundary by design and is always audited against the subject.
        (IamPermissionKeys.UserSupportView, IamCategory.Iam, true),
        // Cross-tenant abuse correlation + automatic-action review queue (S-ADMIN-8a) — crosses the
        // tenant boundary to correlate an actor and can reverse an automatic account action.
        (IamPermissionKeys.TrustSafetyReview, IamCategory.Tenant, true),
        // The network-wide block (S-ADMIN-8b) — bans an actor across EVERY tenant at once and installs a
        // durable deny flag. Deliberately excluded from platform-super-admin's bundle below: a dangerous
        // capability like this is assigned to a NAMED operator via its own role, never inherited wholesale.
        (IamPermissionKeys.NetworkBlockManage, IamCategory.Tenant, true),
        // Re-connecting/replacing the shared platform bot (S-BOT-PLATFORM-UI) — platform-wide by nature
        // (every channel without its own custom bot resolves through this identity), so its own key.
        (IamPermissionKeys.PlatformBotManage, IamCategory.Iam, true),
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
                IamPermissionKeys.BillingWrite,
                IamPermissionKeys.BillingRefund,
                IamPermissionKeys.PlatformAnalyticsRead,
                IamPermissionKeys.ComplianceErasure,
                IamPermissionKeys.GalleryReview,
                IamPermissionKeys.SystemIpcManage,
                IamPermissionKeys.UserImpersonate,
                IamPermissionKeys.ContentRead,
                IamPermissionKeys.ContentAuthor,
                IamPermissionKeys.ContentPublish,
                IamPermissionKeys.ContentPublishForce,
                IamPermissionKeys.TenantQuotaManage,
                IamPermissionKeys.TenantRemigrate,
                IamPermissionKeys.TenantErase,
                IamPermissionKeys.UserSupportView,
                IamPermissionKeys.TrustSafetyReview,
            ]
        ),
        (
            "platform-content-author",
            [
                IamPermissionKeys.ContentRead,
                IamPermissionKeys.ContentAuthor,
                IamPermissionKeys.ContentPublish,
            ]
        ),
        (
            // Act-as impersonation is a restricted, owner-only support tool (S089a) — deliberately NOT
            // bundled here even though platform-support otherwise covers audited tenant access.
            "platform-support",
            [
                IamPermissionKeys.TenantRead,
                IamPermissionKeys.TenantAccess,
                IamPermissionKeys.AuditRead,
                IamPermissionKeys.PlatformAnalyticsRead,
                // The support desk's own reason to exist: find the person a ticket is about and read their
                // real state. A READ — act-as (user:impersonate) stays owner-only, deliberately unbundled.
                IamPermissionKeys.UserSupportView,
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
                IamPermissionKeys.ComplianceErasure,
                // The trust & safety team's own reason to exist: correlate an actor across tenants and
                // review the platform's own automatic spam-defence account actions.
                IamPermissionKeys.TrustSafetyReview,
            ]
        ),
        (
            // The network-wide block's own, deliberately narrow role (S-ADMIN-8b) — carries ONLY
            // network:block:manage, never bundled into platform-trust-safety or platform-super-admin.
            // Assigning it is how the capability reaches a NAMED operator instead of an entire team.
            "platform-network-block",
            [IamPermissionKeys.NetworkBlockManage]
        ),
        (
            // The shared platform bot's own, deliberately narrow role — carries ONLY platform:bot:manage,
            // never bundled into platform-super-admin. A swap here changes what every dependent channel
            // resolves to, so it reaches a NAMED operator via its own role, same law as network-block.
            "platform-bot-admin",
            [IamPermissionKeys.PlatformBotManage]
        ),
        (
            "platform-billing",
            [
                IamPermissionKeys.BillingRead,
                IamPermissionKeys.BillingWrite,
                IamPermissionKeys.BillingRefund,
            ]
        ),
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
                role = new() { Name = roleName, IsSystem = true };
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
                        new() { RoleId = role.Id, PermissionId = permission.Id }
                    );
            }
        }
    }
}
