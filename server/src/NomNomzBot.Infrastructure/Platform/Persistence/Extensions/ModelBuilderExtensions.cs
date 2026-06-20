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
    /// Applies the composing tenant + soft-delete global query filters across every mapped entity
    /// (schema §1.2, platform-conventions §7). The tenant predicate reads the ambient
    /// <paramref name="currentBroadcasterId"/> (the captured <c>AppDbContext</c> accessor) at query time:
    /// a tenant-scoped row is visible only when its <c>BroadcasterId</c> equals the current tenant OR no
    /// tenant is set (background / cross-tenant contexts read all). Soft-deletable rows additionally require
    /// <c>DeletedAt == null</c>. The two predicates compose into one filter so EF10 does not drop one when
    /// both apply to the same entity. <c>IgnoreQueryFilters()</c> bypasses both (e.g. the identity resolver).
    /// </summary>
    public static void ApplyTenantAndSoftDeleteFilters(
        this ModelBuilder modelBuilder,
        Func<Guid?> currentBroadcasterId
    )
    {
        foreach (
            Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes()
        )
        {
            Type clrType = entityType.ClrType;
            bool isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(clrType);
            bool isSoftDeletable = typeof(SoftDeletableEntity).IsAssignableFrom(clrType);

            if (!isTenantScoped && !isSoftDeletable)
                continue;

            ParameterExpression parameter = Expression.Parameter(clrType, "e");
            Expression? body = null;

            // The tenant column is a public mapped Guid property — usually `BroadcasterId`, but some
            // entities expose `ITenantScoped.BroadcasterId` as an EXPLICIT interface implementation over a
            // differently-named public key (e.g. ChannelModerator → `ChannelId`). Bind the filter to the
            // real public property; if none is mappable, skip the tenant predicate (soft-delete still applies).
            System.Reflection.PropertyInfo? tenantProperty = isTenantScoped
                ? clrType.GetProperty(nameof(ITenantScoped.BroadcasterId))
                    ?? clrType.GetProperty("ChannelId")
                : null;

            if (isTenantScoped && tenantProperty?.PropertyType == typeof(Guid))
            {
                // e.<tenant> == currentBroadcasterId() || currentBroadcasterId() == null
                MemberExpression broadcasterId = Expression.Property(parameter, tenantProperty);
                MethodCallExpression currentCall = Expression.Call(
                    Expression.Constant(currentBroadcasterId.Target),
                    currentBroadcasterId.Method
                );
                Expression currentValue = Expression.Convert(currentCall, typeof(Guid?));

                Expression tenantMatch = Expression.Equal(
                    Expression.Convert(broadcasterId, typeof(Guid?)),
                    currentValue
                );
                Expression noTenant = Expression.Equal(
                    currentValue,
                    Expression.Constant(null, typeof(Guid?))
                );
                body = Expression.OrElse(tenantMatch, noTenant);
            }

            if (isSoftDeletable)
            {
                MemberExpression deletedAt = Expression.Property(
                    parameter,
                    nameof(SoftDeletableEntity.DeletedAt)
                );
                Expression notDeleted = Expression.Equal(
                    deletedAt,
                    Expression.Constant(null, typeof(DateTime?))
                );
                body = body is null ? notDeleted : Expression.AndAlso(body, notDeleted);
            }

            LambdaExpression filter = Expression.Lambda(body!, parameter);
            modelBuilder.Entity(clrType).HasQueryFilter(filter);
        }
    }
}
