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
