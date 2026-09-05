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
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Builds the machine-readable JSON export of every row a tenant owns (S-ADMIN-3) — every table
/// <see cref="ChannelBlastRadiusSources"/> already curates for the same channel, so the export and the
/// delete-preview it accompanies can never disagree about which tables belong to a channel. Read-only:
/// materializes each table's live (query-filter-honoring) rows for the broadcaster, untracked.
/// </summary>
internal static class TenantExport
{
    public static Task<string> BuildAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTime exportedAtUtc,
        CancellationToken ct
    ) =>
        BuildAsync(
            db,
            broadcasterId,
            exportedAtUtc,
            ChannelBlastRadiusSources.All.Select(s => s.EntityType).Distinct(),
            ct
        );

    // The entity-type list is injectable so a test can exercise the export over a focused relational schema
    // (mirroring ChannelDeletePreviewService's own internal constructor overload) — production always gets
    // every tenant-scoped table ChannelBlastRadiusSources curates.
    internal static async Task<string> BuildAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTime exportedAtUtc,
        IEnumerable<Type> entityTypes,
        CancellationToken ct
    )
    {
        SortedDictionary<string, object?> tables = new(StringComparer.Ordinal);
        foreach (Type entityType in entityTypes)
        {
            PropertyInfo? tenantProperty = TenantKey.ResolveProperty(entityType);
            if (tenantProperty is null)
                continue;

            MethodInfo method = FetchMethod.MakeGenericMethod(entityType);
            Task<object?> task =
                (Task<object?>)method.Invoke(null, [db, tenantProperty, broadcasterId, ct])!;
            tables[entityType.Name] = await task;
        }

        return JsonConvert.SerializeObject(
            new
            {
                BroadcasterId = broadcasterId,
                ExportedAtUtc = exportedAtUtc,
                Tables = tables,
            },
            Formatting.Indented
        );
    }

    private static readonly MethodInfo FetchMethod = typeof(TenantExport).GetMethod(
        nameof(FetchAsync),
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static async Task<object?> FetchAsync<TEntity>(
        IApplicationDbContext db,
        PropertyInfo tenantProperty,
        Guid broadcasterId,
        CancellationToken ct
    )
        where TEntity : class
    {
        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        ConstantExpression value =
            tenantProperty.PropertyType == typeof(Guid?)
                ? Expression.Constant(broadcasterId, typeof(Guid?))
                : Expression.Constant(broadcasterId);
        Expression<Func<TEntity, bool>> predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(Expression.Property(parameter, tenantProperty), value),
            parameter
        );

        List<TEntity> rows = await ((DbContext)db)
            .Set<TEntity>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(ct);
        return rows;
    }
}
