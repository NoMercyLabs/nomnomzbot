// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Integrations.Services;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// Process-lifetime observation store behind <see cref="IIntegrationCapabilityStore"/>. Singleton;
/// safe for concurrent providers/pollers. Restart resets to "unknown" (absent), which is the honest
/// state until the next observation.
/// </summary>
public sealed class InMemoryIntegrationCapabilityStore : IIntegrationCapabilityStore
{
    private readonly ConcurrentDictionary<
        (Guid BroadcasterId, string Provider),
        ConcurrentDictionary<string, bool>
    > _observations = new();

    public void Report(Guid broadcasterId, string provider, string capability, bool supported)
    {
        ConcurrentDictionary<string, bool> forProvider = _observations.GetOrAdd(
            (broadcasterId, Normalize(provider)),
            _ => new ConcurrentDictionary<string, bool>(StringComparer.Ordinal)
        );
        forProvider[capability] = supported;
    }

    public IReadOnlyDictionary<string, bool> GetObserved(Guid broadcasterId, string provider) =>
        _observations.TryGetValue(
            (broadcasterId, Normalize(provider)),
            out ConcurrentDictionary<string, bool>? forProvider
        )
            ? new Dictionary<string, bool>(forProvider)
            : new Dictionary<string, bool>();

    private static string Normalize(string provider) => provider.ToLowerInvariant();
}
