// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
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
///
/// Also cascades every configured <c>DeleteBehavior.SetNull</c>/<c>ClientSetNull</c> relationship
/// (§1.3: "never hard-DELETE") the moment an entity is newly soft-deleted. A soft delete never becomes
/// a real DELETE statement, so the database's own ON DELETE SET NULL never fires — a dependent left
/// pointing at a soft-deleted row keeps the dangling id forever, and the query filter hiding the
/// deleted row makes it silently disappear from whatever resolved it (confirmed live, 2026-09-14:
/// qtkitte's <c>!spank</c> command pointed at a soft-deleted Pipeline and no-opped on every trigger
/// with no error). Reads the FK straight from EF's own model metadata — the same
/// <c>.OnDelete(DeleteBehavior.SetNull)</c> each entity configuration already declares — so this covers
/// every current AND future such relationship in one place, not a hand-maintained list of entity types
/// to remember to update. Every current relationship of this shape keys on <see cref="Guid"/> (this
/// codebase's uniform id type); anything else is skipped rather than guessed at.
/// </summary>
public sealed class SoftDeleteInterceptor(
    TimeProvider timeProvider,
    ICurrentUserService currentUser
) : SaveChangesInterceptor
{
    private static readonly MethodInfo NullifyDependentsMethod =
        typeof(SoftDeleteInterceptor).GetMethod(
            nameof(NullifyDependentsAsync),
            BindingFlags.NonPublic | BindingFlags.Static
        ) ?? throw new InvalidOperationException($"{nameof(NullifyDependentsAsync)} not found.");

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            List<EntityEntry<SoftDeletableEntity>> newlyDeleted = ConvertDeleteToSoftDelete(
                eventData.Context
            );
            await CascadeSetNullAsync(eventData.Context, newlyDeleted, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is not null)
        {
            // The SetNull cascade below is async-only (it queries dependent rows) and this codebase
            // never calls sync SaveChanges (house rule: async all the way, never .Result/.Wait() —
            // no call site does), so there is nothing to cascade from here. Soft-delete stamping
            // itself still applies for completeness.
            ConvertDeleteToSoftDelete(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private List<EntityEntry<SoftDeletableEntity>> ConvertDeleteToSoftDelete(DbContext context)
    {
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        List<EntityEntry<SoftDeletableEntity>> newlyDeleted = [];

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
                newlyDeleted.Add(entry);
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
                newlyDeleted.Add(entry);
            }
            else if (wasDeleted && !isDeleted)
            {
                // Restored — a live row carries no "deleted by".
                entry.Entity.DeletedBy = null;
            }
        }

        return newlyDeleted;
    }

    private static async Task CascadeSetNullAsync(
        DbContext context,
        List<EntityEntry<SoftDeletableEntity>> newlyDeleted,
        CancellationToken ct
    )
    {
        foreach (EntityEntry<SoftDeletableEntity> entry in newlyDeleted)
        {
            IKey? primaryKey = entry.Metadata.FindPrimaryKey();
            if (primaryKey is null || primaryKey.Properties.Count != 1)
                continue; // every entity in this model has a single-column PK; defensive only.

            if (entry.Property(primaryKey.Properties[0].Name).CurrentValue is not Guid principalId)
                continue;

            foreach (IForeignKey foreignKey in entry.Metadata.GetReferencingForeignKeys())
            {
                if (
                    foreignKey.DeleteBehavior
                    is not (DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull)
                )
                    continue;
                if (foreignKey.Properties.Count != 1)
                    continue; // every SetNull relationship in this model is single-column.

                Type fkClrType = foreignKey.Properties[0].ClrType;
                if (fkClrType != typeof(Guid) && fkClrType != typeof(Guid?))
                    continue;

                await (Task)
                    NullifyDependentsMethod
                        .MakeGenericMethod(foreignKey.DeclaringEntityType.ClrType)
                        .Invoke(null, [context, foreignKey.Properties[0].Name, principalId, ct])!;
            }
        }
    }

    private static async Task NullifyDependentsAsync<TDependent>(
        DbContext context,
        string fkPropertyName,
        Guid principalId,
        CancellationToken ct
    )
        where TDependent : class
    {
        List<TDependent> dependents = await context
            .Set<TDependent>()
            .Where(e => EF.Property<Guid?>(e, fkPropertyName) == principalId)
            .ToListAsync(ct);

        foreach (TDependent dependent in dependents)
        {
            context.Entry(dependent).Property(fkPropertyName).CurrentValue = null;
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
