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
using NomNomzBot.Application.AutomationApi.Services;

namespace NomNomzBot.Infrastructure.AutomationApi.Stream;

/// <summary>Singleton in-memory session book for the automation stream (automation-api.md §3/D9).</summary>
public sealed class AutomationSessionRegistry : IAutomationSessionRegistry
{
    private readonly ConcurrentDictionary<string, AutomationSession> _sessions = new();

    public void Register(AutomationSession session) => _sessions[session.SessionId] = session;

    public void Unregister(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public IReadOnlyCollection<AutomationSession> SubscribersOf(string publicEventName) =>
        [.. _sessions.Values.Where(s => s.IsSubscribedTo(publicEventName))];
}
