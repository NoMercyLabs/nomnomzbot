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

using NomNomzBot.Application.AutomationApi.Dtos;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// One live automation WebSocket connection (automation-api.md §4.2/D9). Sessions are in-memory and
/// per-node — the cluster-wide event bus means every node serves its own sockets. Subscription
/// patterns match catalog wire names exactly, or by wildcard (<c>*</c>, <c>Custom.*</c>).
/// </summary>
public sealed class AutomationSession
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _patterns = [];

    public required string SessionId { get; init; }

    public required AutomationPrincipal Principal { get; init; }

    /// <summary>Writes one text frame to the socket; the connection serializes concurrent sends.</summary>
    public required Func<string, CancellationToken, Task> SendAsync { get; init; }

    public void Subscribe(IEnumerable<string> patterns)
    {
        lock (_gate)
        {
            foreach (string pattern in patterns)
                _patterns.Add(pattern);
        }
    }

    public bool IsSubscribedTo(string publicEventName)
    {
        lock (_gate)
        {
            foreach (string pattern in _patterns)
            {
                if (pattern == "*" || pattern == publicEventName)
                    return true;
                if (
                    pattern.EndsWith(".*", StringComparison.Ordinal)
                    && publicEventName.StartsWith(
                        pattern[..^1], // "Custom.*" → "Custom."
                        StringComparison.Ordinal
                    )
                )
                    return true;
            }
            return false;
        }
    }
}

/// <summary>In-memory per-node session tracking for the automation event stream (automation-api.md §3/D9).</summary>
public interface IAutomationSessionRegistry
{
    void Register(AutomationSession session);

    void Unregister(string sessionId);

    /// <summary>Sessions whose subscription patterns match <paramref name="publicEventName"/> (scope/tenant filtering is the caller's).</summary>
    IReadOnlyCollection<AutomationSession> SubscribersOf(string publicEventName);
}
