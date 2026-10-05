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

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Remembers which Twitch event sessions are in an unplanned outage and which channels were told, so a
/// retry storm posts ONE drop line per outage and the recovery line goes only to the chats that heard the
/// drop. In-memory on purpose: a restart ends the outage (the new process opens fresh sessions).
/// </summary>
public sealed class EventSubOutageLedger
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<Guid>> _outages = new();

    /// <summary>True for the first drop of an outage; false while that outage is still open.</summary>
    public bool TryBegin(Guid ownerId) => _outages.TryAdd(ownerId, []);

    /// <summary>Notes the channels that heard the drop line of the open outage.</summary>
    public void RecordAnnounced(Guid ownerId, IReadOnlyList<Guid> channelIds)
    {
        if (_outages.TryGetValue(ownerId, out IReadOnlyList<Guid>? open))
            _outages.TryUpdate(ownerId, channelIds, open);
    }

    /// <summary>Closes the outage and returns the channels to tell; empty when no outage was open.</summary>
    public IReadOnlyList<Guid> End(Guid ownerId) =>
        _outages.TryRemove(ownerId, out IReadOnlyList<Guid>? channelIds) ? channelIds : [];
}
