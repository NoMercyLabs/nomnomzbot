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

using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// Records one platform-default change into the platform-IAM audit log — the same table feature-flag changes
/// use: <c>Permission</c> names the default family, <c>TargetResource</c> the edited key, <c>Justification</c>
/// the before/after values, <c>AffectedTenantCount</c> the counted blast radius the operator confirmed.
/// Tracked on the change tracker, so it commits with the caller's own <c>SaveChangesAsync</c>.
/// </summary>
internal static class PlatformDefaultAudit
{
    public static void Record(
        IApplicationDbContext db,
        string family,
        string targetKey,
        Guid actorUserId,
        string oldValue,
        string newValue,
        int affectedChannels,
        DateTime now
    ) =>
        db.IamAuditLogs.Add(
            new()
            {
                PrincipalId = actorUserId,
                PrincipalType = IamPrincipalType.Employee,
                Permission = $"platform_default:{family}",
                TargetResource = targetKey,
                Justification = $"old={oldValue};new={newValue}",
                AffectedTenantCount = affectedChannels,
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
            }
        );
}
