// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;

namespace NomNomzBot.Infrastructure.AutomationApi.Events;

/// <summary>
/// The public event catalog (automation-api.md §3/D6) — built once at startup from every registered
/// <see cref="IAutomationEventDescriptor"/> (DI assembly scan). Fails fast on a duplicate wire name
/// or a duplicate domain-event type: two descriptors claiming the same thing is a wiring bug, not a
/// runtime condition.
/// </summary>
public sealed class AutomationEventRegistry : IAutomationEventRegistry
{
    private readonly Dictionary<Type, IAutomationEventDescriptor> _byType;

    public AutomationEventRegistry(IEnumerable<IAutomationEventDescriptor> descriptors)
    {
        List<IAutomationEventDescriptor> all = [.. descriptors];

        List<string> duplicateNames =
        [
            .. all.GroupBy(d => d.PublicName).Where(g => g.Count() > 1).Select(g => g.Key),
        ];
        if (duplicateNames.Count > 0)
            throw new InvalidOperationException(
                $"Duplicate automation event name(s): {string.Join(", ", duplicateNames)}."
            );

        _byType = new Dictionary<Type, IAutomationEventDescriptor>();
        foreach (IAutomationEventDescriptor descriptor in all)
        {
            if (!_byType.TryAdd(descriptor.DomainEventType, descriptor))
                throw new InvalidOperationException(
                    $"Two automation descriptors claim {descriptor.DomainEventType.Name}."
                );
        }

        Catalog =
        [
            .. all.OrderBy(d => d.PublicName, StringComparer.Ordinal)
                .Select(d => new AutomationEventCatalogItem(d.PublicName, d.Description)),
        ];
    }

    public IReadOnlyList<AutomationEventCatalogItem> Catalog { get; }

    public bool TryGet(
        Type domainEventType,
        [MaybeNullWhen(false)] out IAutomationEventDescriptor descriptor
    ) => _byType.TryGetValue(domainEventType, out descriptor!);
}
