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
/// Automatically sets CreatedAt and UpdatedAt timestamps on BaseEntity instances
/// before saving changes to the database. Reads the current time from the injected
/// TimeProvider (the single clock, platform-conventions §3.11) so stamping is fakeable.
/// </summary>
public sealed class AuditableEntityInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            UpdateAuditFields(eventData.Context);
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
            UpdateAuditFields(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void UpdateAuditFields(DbContext context)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (EntityEntry<BaseEntity> entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.UpdatedAt = utcNow;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = utcNow;
                    // Prevent overwriting CreatedAt on updates
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    break;
            }
        }
    }
}
