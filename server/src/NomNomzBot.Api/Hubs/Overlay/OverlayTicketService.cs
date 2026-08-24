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
using System.Security.Cryptography;

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
        (Guid BroadcasterId, DateTimeOffset ExpiresAt)
    > _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public OverlayTicketService(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public string IssueTicket(Guid broadcasterId)
    {
        PruneExpired();
        string ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (broadcasterId, _timeProvider.GetUtcNow() + TicketLifetime);
        return ticket;
    }

    public Guid? RedeemTicket(string? ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return null;

        if (!_tickets.TryRemove(ticket, out (Guid BroadcasterId, DateTimeOffset ExpiresAt) entry))
            return null;

        return entry.ExpiresAt >= _timeProvider.GetUtcNow() ? entry.BroadcasterId : null;
    }

    private void PruneExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (
            KeyValuePair<string, (Guid BroadcasterId, DateTimeOffset ExpiresAt)> entry in _tickets
        )
            if (entry.Value.ExpiresAt < now)
                _tickets.TryRemove(entry.Key, out _);
    }
}
