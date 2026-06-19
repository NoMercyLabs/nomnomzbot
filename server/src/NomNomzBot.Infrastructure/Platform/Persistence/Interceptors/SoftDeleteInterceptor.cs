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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

/// <summary>
/// Intercepts Remove() calls on SoftDeletableEntity instances and converts them
/// to soft deletes by setting DeletedAt instead of physically deleting the row.
/// </summary>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
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

    private static void ConvertDeleteToSoftDelete(DbContext context)
    {
        DateTime utcNow = DateTime.UtcNow;

        foreach (
            EntityEntry<SoftDeletableEntity> entry in context.ChangeTracker.Entries<SoftDeletableEntity>()
        )
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            // Convert hard delete to soft delete
            entry.State = EntityState.Modified;
            entry.Entity.DeletedAt = utcNow;
            entry.Entity.UpdatedAt = utcNow;
        }
    }
}
