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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// The auto-discovered <see cref="IEventSubEventTranslator"/> set, indexed by subscription type
/// (twitch-eventsub §3.7). Singleton: translators are stateless, so the map is built once at composition. Two
/// translators claiming the same subscription type is a wiring bug, not a runtime condition — it throws at
/// construction (fail fast at boot) rather than silently letting one shadow the other.
/// </summary>
public sealed class EventSubTranslatorRegistry : IEventSubTranslatorRegistry
{
    private readonly IReadOnlyDictionary<string, IEventSubEventTranslator> _byType;

    public EventSubTranslatorRegistry(IEnumerable<IEventSubEventTranslator> translators)
    {
        Dictionary<string, IEventSubEventTranslator> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (IEventSubEventTranslator translator in translators)
        {
            if (!map.TryAdd(translator.SubscriptionType, translator))
                throw new InvalidOperationException(
                    $"Two EventSub translators claim subscription type '{translator.SubscriptionType}': "
                        + $"'{map[translator.SubscriptionType].GetType().Name}' and '{translator.GetType().Name}'."
                );
        }

        _byType = map;
    }

    public bool TryGet(
        string subscriptionType,
        [NotNullWhen(true)] out IEventSubEventTranslator? translator
    ) => _byType.TryGetValue(subscriptionType, out translator);
}
