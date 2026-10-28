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
using System.Security.Cryptography;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// In-memory <see cref="IOverlayTicketService"/>. Single-replica by design (the multi-replica backplane is
/// out of scope for S035 — 🔒 owner decision); a ticket only needs to survive the few seconds between the
/// SDK's ticket fetch and its immediately-following WebSocket connect on the SAME instance that issued it.
/// </summary>
public sealed class OverlayTicketService : IOverlayTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<
        string,
        (OverlayTokenScope Scope, DateTimeOffset ExpiresAt)
    > _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public OverlayTicketService(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public string IssueTicket(OverlayTokenScope scope)
    {
        PruneExpired();
        string ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (scope, _timeProvider.GetUtcNow() + TicketLifetime);
        return ticket;
    }

    public OverlayTokenScope? RedeemTicket(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return null;

        if (
            !_tickets.TryRemove(
                ticket,
                out (OverlayTokenScope Scope, DateTimeOffset ExpiresAt) entry
            )
        )
            return null;

        return entry.ExpiresAt >= _timeProvider.GetUtcNow() ? entry.Scope : null;
    }

    private void PruneExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (
            KeyValuePair<
                string,
                (OverlayTokenScope Scope, DateTimeOffset ExpiresAt)
            > entry in _tickets
        )
            if (entry.Value.ExpiresAt < now)
                _tickets.TryRemove(entry.Key, out _);
    }
}
