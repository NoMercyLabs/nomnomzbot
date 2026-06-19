// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies a global query filter for soft-deletable entities: WHERE DeletedAt IS NULL.
    /// </summary>
    public static void ApplySoftDeleteFilter<TEntity>(this ModelBuilder modelBuilder)
        where TEntity : SoftDeletableEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.DeletedAt == null);
    }

    /// <summary>
    /// Applies a global query filter for tenant-scoped entities.
    /// Requires ICurrentTenantService to be resolved at query time via a DbContext parameter.
    /// This is a no-op placeholder; tenant filtering is applied per-query or via interceptor.
    /// </summary>
    public static void ApplyTenantFilter<TEntity>(
        this ModelBuilder modelBuilder,
        Expression<Func<TEntity, bool>> filter
    )
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
    }
}
