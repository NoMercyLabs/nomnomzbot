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
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

/// <summary>
/// Intercepts Remove() calls on SoftDeletableEntity instances and converts them to soft deletes by
/// setting DeletedAt instead of physically deleting the row. Reads the current time from the
/// injected TimeProvider (the single clock, platform-conventions §3.11) so the soft-delete stamp is
/// fakeable.
///
/// Also stamps <see cref="SoftDeletableEntity.DeletedBy"/> (S013d) — WHO deleted a row, not just
/// WHEN — for every soft delete, whether it arrived via <c>Remove()</c> (converted above) or a
/// service setting <c>DeletedAt</c> directly (the majority of call sites: they load the row, flip
/// the flag, and call <c>SaveChangesAsync</c> without ever calling <c>Remove()</c>). Detecting the
/// null→non-null transition on the tracked <c>DeletedAt</c> property here — one seam, not 84
/// per-entity edits — covers both shapes uniformly. The actor is <see cref="ICurrentUserService"/>'s
/// impersonation-aware identity: during an act-as session the OPERATOR is stamped, never the
/// impersonated subject, matching the journal-write convention (S089c). A restore (DeletedAt reset
/// to null) clears DeletedBy back to null — a live row has no "deleted by".
/// </summary>
public sealed class SoftDeleteInterceptor(
    TimeProvider timeProvider,
    ICurrentUserService currentUser
) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            ConvertDeleteToSoftDelete(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is not null)
        {
            ConvertDeleteToSoftDelete(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void ConvertDeleteToSoftDelete(DbContext context)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (
            EntityEntry<SoftDeletableEntity> entry in context.ChangeTracker.Entries<SoftDeletableEntity>()
        )
        {
            if (entry.State == EntityState.Deleted)
            {
                // Convert hard delete to soft delete
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = utcNow;
                entry.Entity.UpdatedAt = utcNow;
                entry.Entity.DeletedBy = ActingUserId();
                continue;
            }

            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            // A service that loaded the row and flipped DeletedAt itself (the majority shape —
            // no Remove() call) lands here as an ordinary property change. Compare against the
            // ORIGINAL value, not just "is it non-null now", so an update that merely re-saves an
            // already-deleted row doesn't re-stamp DeletedBy with whoever happens to be acting now.
            PropertyEntry<SoftDeletableEntity, DateTime?> deletedAtProperty = entry.Property(e =>
                e.DeletedAt
            );
            bool wasDeleted = deletedAtProperty.OriginalValue is not null;
            bool isDeleted = deletedAtProperty.CurrentValue is not null;

            if (!wasDeleted && isDeleted)
            {
                entry.Entity.DeletedBy = ActingUserId();
            }
            else if (wasDeleted && !isDeleted)
            {
                // Restored — a live row carries no "deleted by".
                entry.Entity.DeletedBy = null;
            }
        }
    }

    private Guid? ActingUserId()
    {
        // Impersonation (S089c convention): attribute the real platform OPERATOR who ran the
        // support session, never the impersonated subject whose data is being touched.
        if (currentUser.Impersonation is { } impersonation)
        {
            return impersonation.OperatorUserId;
        }

        return Guid.TryParse(currentUser.UserId, out Guid userId) ? userId : null;
    }
}
