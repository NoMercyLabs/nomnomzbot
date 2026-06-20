// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Transforms an older serialized event shape into the next version's shape, ON READ. The journal is
/// immutable — historical rows keep their original <c>EventVersion</c>/<c>Payload</c> forever; on read the
/// store chains upcasters (<c>v1→v2→v3</c>) until the payload reaches the current version, so projections and
/// replay only ever see the current shape. Pure: no side effects, keyed by <c>(EventType, FromVersion)</c>.
/// </summary>
public interface IEventUpcaster
{
    string EventType { get; }

    /// <summary>Transforms <c>FromVersion</c> → <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>Pure transform of the JSON payload from one version to the next.</summary>
    Result<string> Upcast(string payloadJson);
}

/// <summary>
/// Builds and applies the upcaster chain for an event type. Brings a stored payload from its recorded version
/// up to the current version, so all read/replay sees the current shape. Read-only/pure.
/// </summary>
public interface IEventUpcasterRegistry
{
    /// <summary>
    /// Applies the chain of registered upcasters to bring a payload from <paramref name="fromVersion"/> to the
    /// current version for <paramref name="eventType"/>. Returns the payload unchanged when already current.
    /// </summary>
    Result<UpcastResult> UpcastToCurrent(string eventType, int fromVersion, string payloadJson);

    /// <summary>
    /// Current (highest) known version for an event type — the version new appends are stamped with. 1 when no
    /// upcaster is registered for the type (the implicit baseline).
    /// </summary>
    int CurrentVersion(string eventType);
}
