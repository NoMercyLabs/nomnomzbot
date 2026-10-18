// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// Tells a channel's open dashboards that its event journal grew, so the journal page reads it again instead
/// of polling. Signals are coalesced per channel: a burst of appends produces one push.
/// </summary>
public interface IEventJournalChangeNotifier
{
    /// <summary>Signals that <paramref name="channelId"/>'s journal has new rows.</summary>
    void NotifyChanged(Guid channelId);
}
