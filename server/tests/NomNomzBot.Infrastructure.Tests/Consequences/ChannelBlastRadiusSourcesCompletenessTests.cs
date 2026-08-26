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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// The channel-delete preview groups tables into six curated categories plus a remainder. That grouping is a
/// hand-audited decision, which means it can only stay honest if it is also EXHAUSTIVE — a table nobody
/// assigned would simply vanish from the number the operator is shown. These tests derive the truth from
/// <see cref="IApplicationDbContext"/> by reflection, so adding a tenant-scoped table without deciding which
/// category its rows die in fails the build.
/// </summary>
public class ChannelBlastRadiusSourcesCompletenessTests
{
    private static IReadOnlySet<Type> TenantScopedEntities =>
        typeof(IApplicationDbContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            )
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(t => TenantKey.ResolveProperty(t) is not null)
            .ToHashSet();

    [Fact]
    public void Sources_CoverEveryTenantScopedTable()
    {
        HashSet<Type> covered = [.. ChannelBlastRadiusSources.All.Select(s => s.EntityType)];
        List<string> missing =
        [
            .. TenantScopedEntities.Except(covered).Select(t => t.Name).Order(),
        ];

        Assert.True(
            missing.Count == 0,
            "Every table that carries a channel id dies with the channel and must be assigned to a "
                + "blast-radius category (a curated one, or the 'other' remainder). Unassigned: "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Sources_CountNoTableTwice()
    {
        List<string> duplicated =
        [
            .. ChannelBlastRadiusSources
                .All.GroupBy(s => s.EntityType)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.Name)
                .Order(),
        ];

        // A table counted twice inflates the total, which is just as dishonest as omitting one.
        Assert.Empty(duplicated);
    }

    [Fact]
    public void Sources_CountNothingThatIsNotTenantScoped()
    {
        List<string> foreign =
        [
            .. ChannelBlastRadiusSources
                .All.Select(s => s.EntityType)
                .Where(t => !TenantScopedEntities.Contains(t))
                .Select(t => t.Name)
                .Order(),
        ];

        Assert.True(foreign.Count == 0, string.Join(", ", foreign));
    }

    [Fact]
    public void Sources_UseOnlyTheDeclaredCategoryKeys()
    {
        HashSet<string> allowed =
        [
            BlastRadiusCategoryKeys.ChannelChat,
            BlastRadiusCategoryKeys.ChannelViewers,
            BlastRadiusCategoryKeys.ChannelAutomations,
            BlastRadiusCategoryKeys.ChannelIntegrations,
            BlastRadiusCategoryKeys.ChannelOverlays,
            BlastRadiusCategoryKeys.ChannelBilling,
            BlastRadiusCategoryKeys.ChannelOther,
        ];

        // The dashboard renders an unknown key as a generic "N other records" line. That fallback exists so a
        // category is never silently DROPPED — it is not a place to invent new keys.
        Assert.Empty(ChannelBlastRadiusSources.All.Select(s => s.CategoryKey).Except(allowed));
    }
}
