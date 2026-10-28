// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
            _ => new(StringComparer.Ordinal)
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
