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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Platform.Persistence;

/// <summary>
/// Guards the core multi-tenant invariant (schema §1.2): the global tenant query filter is applied only to
/// entities implementing <see cref="ITenantScoped"/>. An entity that carries a non-nullable
/// <c>Guid BroadcasterId</c> but forgets the interface would receive NO tenant filter — every channel's rows
/// would be visible to every tenant (a silent cross-tenant data leak). This reflects over the actual mapped
/// entity set (AppDbContext's <c>DbSet&lt;T&gt;</c> properties) so a future entity that breaks the rule fails CI.
/// </summary>
public sealed class TenantIsolationCoverageTests
{
    [Fact]
    public void Every_mapped_entity_with_a_non_nullable_BroadcasterId_implements_ITenantScoped()
    {
        IReadOnlyList<Type> mappedEntities = typeof(AppDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            )
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToList();

        // Sanity: the reflection actually found the entity set (guards against a silently-empty assertion).
        mappedEntities.Should().HaveCountGreaterThan(50);

        // TenantSequence is the single intentional exception: it is read only via the allocator's raw
        // `... WHERE BroadcasterId = {x} FOR UPDATE` query, so an ambient-tenant ORM filter would wrongly
        // exclude its row whenever the allocator runs under a different or unset tenant. Its rows are
        // non-sensitive monotonic counters and are never read without that explicit predicate.
        HashSet<string> intentionallyUnfiltered = new(StringComparer.Ordinal) { "TenantSequence" };

        List<string> leaky = [];
        foreach (Type entity in mappedEntities)
        {
            // A non-nullable Guid BroadcasterId means the row belongs to exactly one tenant and MUST be
            // filtered. A nullable Guid? BroadcasterId is an intentionally-global row (e.g. the platform bot
            // connection) and is correctly left unfiltered.
            PropertyInfo? broadcasterId = entity.GetProperty("BroadcasterId");
            bool hasNonNullableTenantKey = broadcasterId?.PropertyType == typeof(Guid);
            bool isTenantScoped = typeof(ITenantScoped).IsAssignableFrom(entity);

            if (
                hasNonNullableTenantKey
                && !isTenantScoped
                && !intentionallyUnfiltered.Contains(entity.Name)
            )
                leaky.Add(entity.Name);
        }

        leaky
            .Should()
            .BeEmpty(
                "every mapped entity with a non-nullable Guid BroadcasterId must implement ITenantScoped so "
                    + "the global tenant query filter isolates it; without the interface its rows are visible "
                    + "to every tenant. Offending entities: {0}",
                string.Join(", ", leaky)
            );
    }
}
