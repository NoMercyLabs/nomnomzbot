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

using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// No-op fallback for a host with no dashboard hub; the API host replaces it with the SignalR-backed
/// <c>EventJournalChangeNotifier</c>, the same pattern as the action-required notifier.
/// </summary>
public sealed class NullEventJournalChangeNotifier : IEventJournalChangeNotifier
{
    public void NotifyChanged(Guid channelId) { }
}
