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
