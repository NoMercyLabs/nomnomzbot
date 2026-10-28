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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Default <see cref="IPlatformOwnerPrincipalMinter"/> implementation. Row-for-row the same logic
/// <c>AuthService</c> used to inline as <c>MintPlatformOwnerPrincipalAsync</c> (D2 bootstrap fix), extracted
/// so <c>IamPrincipalBackfillSeeder</c> can reuse it for pre-existing <c>IsPlatformPrincipal</c> users that
/// predate the D2 fix, instead of duplicating the row-building.
/// </summary>
public sealed class PlatformOwnerPrincipalMinter : IPlatformOwnerPrincipalMinter
{
    // The seeded system role the owner (or the configured initial admin) is minted into on promotion —
    // IamCatalogSeeder's "platform-super-admin" (roles-permissions.md §C.2).
    private const string PlatformOwnerRoleName = "platform-super-admin";

    // The service-account principal attributed as the ACTING principal for bootstrap-time IAM writes, so a
    // bootstrap role assignment is never attributed to Guid.Empty (fix D2 item 3).
    private const string SystemPrincipalName = "system-bootstrap";

    private readonly IApplicationDbContext _db;

    public PlatformOwnerPrincipalMinter(IApplicationDbContext db) => _db = db;

    public async Task MintAsync(Guid userId, CancellationToken ct = default)
    {
        IamPrincipal? principal = await _db.IamPrincipals.FirstOrDefaultAsync(
            p => p.UserId == userId,
            ct
        );
        if (principal is null)
        {
            principal = new()
            {
                PrincipalType = IamPrincipalType.Employee,
                UserId = userId,
                Name = "Platform Owner",
                IsActive = true,
            };
            _db.IamPrincipals.Add(principal);
        }
        else
        {
            principal.IsActive = true;
        }

        IamRole? ownerRole = await _db.IamRoles.FirstOrDefaultAsync(
            r => r.Name == PlatformOwnerRoleName,
            ct
        );
        if (ownerRole is null)
            return; // the IAM catalog seeder has not run yet; the next call retries idempotently

        bool hasAssignment = await _db.IamRoleAssignments.AnyAsync(
            a => a.PrincipalId == principal.Id && a.RoleId == ownerRole.Id && a.RevokedAt == null,
            ct
        );
        if (hasAssignment)
            return;

        Guid systemPrincipalId = await EnsureSystemPrincipalIdAsync(ct);
        _db.IamRoleAssignments.Add(
            new()
            {
                PrincipalId = principal.Id,
                RoleId = ownerRole.Id,
                AssignedByPrincipalId = systemPrincipalId,
                Reason = "self-host owner bootstrap",
            }
        );
    }

    private async Task<Guid> EnsureSystemPrincipalIdAsync(CancellationToken ct)
    {
        IamPrincipal? system = await _db.IamPrincipals.FirstOrDefaultAsync(
            p =>
                p.PrincipalType == IamPrincipalType.ServiceAccount && p.Name == SystemPrincipalName,
            ct
        );
        if (system is not null)
            return system.Id;

        system = new()
        {
            PrincipalType = IamPrincipalType.ServiceAccount,
            Name = SystemPrincipalName,
            IsActive = true,
        };
        _db.IamPrincipals.Add(system);
        return system.Id;
    }
}
