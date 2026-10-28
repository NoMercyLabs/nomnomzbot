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

using System.Reflection;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Maps a journaled <c>EventType</c> string back to the concrete <see cref="DomainEventBase"/> CLR type that
/// produced it — the inverse of <c>EventStoreSubscriber</c>'s <c>typeof(TEvent).Name</c> discriminator. Used
/// to reconstruct a typed event from its stored payload for replay.
/// <para>
/// Deliberately a CLOSED lookup: the scan is restricted to the single assembly that owns
/// <see cref="DomainEventBase"/> (<c>NomNomzBot.Domain</c>) and only concrete, non-abstract subclasses. A
/// journal <c>EventType</c> string is untrusted input (it round-trips through export/import files a caller
/// controls) — this registry NEVER resolves a type from that string via <c>Type.GetType</c> or an
/// assembly-qualified name; it only ever looks up a name against this pre-built, trusted dictionary, so a
/// crafted import can at worst name a type that doesn't exist (a clean lookup miss) and never cause arbitrary
/// type loading.
/// </para>
/// </summary>
public sealed class DomainEventTypeRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _byName;

    public DomainEventTypeRegistry()
    {
        Assembly domainAssembly = typeof(DomainEventBase).Assembly;
        _byName = domainAssembly
            .GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(DomainEventBase).IsAssignableFrom(t)
            )
            .ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
    }

    /// <summary>Resolves an <c>EventType</c> discriminator to its CLR type, or null if unknown (skip, don't guess).</summary>
    public Type? Resolve(string eventType) => _byName.GetValueOrDefault(eventType);
}
