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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Domain.Billing;

namespace NomNomzBot.Application.Tests.Billing;

/// <summary>
/// S-BUDGETS-a's structural guard: every entity type exposed as a <c>DbSet&lt;T&gt;</c> on
/// <see cref="IApplicationDbContext"/> that carries <c>[CountedResource]</c> — the real source, not a
/// hand-maintained list — MUST have a matching, consistently-classified entry in
/// <see cref="LimitedResourceRegistry"/>. A newly shipped limited resource that forgets to register itself
/// fails this test loudly, by name, instead of silently presenting an unenforced limit as real.
/// </summary>
public sealed class LimitedResourceGuardTests
{
    /// <summary>Every entity type structurally reachable via a <c>DbSet&lt;T&gt;</c> property on the real DbContext.</summary>
    private static IReadOnlyList<Type> DbSetEntityTypes() =>
        [
            .. typeof(IApplicationDbContext)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                )
                .Select(p => p.PropertyType.GetGenericArguments()[0]),
        ];

    [Fact]
    public void Every_CountedResource_entity_has_a_matching_registry_entry()
    {
        List<Type> entityTypes = [.. DbSetEntityTypes()];
        entityTypes.Should().NotBeEmpty("the scan must actually walk real DbSet<T> properties");

        List<(Type EntityType, CountedResourceAttribute Attribute)> declared =
        [
            .. entityTypes
                .Select(t =>
                    (EntityType: t, Attribute: t.GetCustomAttribute<CountedResourceAttribute>())
                )
                .Where(x => x.Attribute is not null)
                .Select(x => (x.EntityType, x.Attribute!)),
        ];

        // FAIL LOUD, not silently skip: every declared resource must resolve, by key, to a registry entry
        // whose classification matches the entity's own declaration.
        foreach ((Type entityType, CountedResourceAttribute attribute) in declared)
        {
            LimitedResourceRegistry
                .TryGet(attribute.LimitKey, out LimitedResourceDescriptor descriptor)
                .Should()
                .BeTrue(
                    $"{entityType.FullName} declares [CountedResource(\"{attribute.LimitKey}\")] "
                        + "but LimitedResourceRegistry has no matching entry — an unenforced limit "
                        + "presented as real is the exact bug S-BUDGETS-a exists to close"
                );

            descriptor
                .Class.Should()
                .Be(
                    attribute.Class,
                    $"{entityType.FullName}'s [CountedResource] classification must match its "
                        + $"LimitedResourceRegistry entry for '{attribute.LimitKey}'"
                );
        }
    }

    [Fact]
    public void Every_CountedResource_declaration_uses_a_unique_limit_key()
    {
        List<CountedResourceAttribute> declared =
        [
            .. DbSetEntityTypes()
                .Select(t => t.GetCustomAttribute<CountedResourceAttribute>())
                .OfType<CountedResourceAttribute>(),
        ];

        declared
            .Select(a => a.LimitKey)
            .Should()
            .OnlyHaveUniqueItems("two entities sharing one limit key would double-count usage");
    }
}
