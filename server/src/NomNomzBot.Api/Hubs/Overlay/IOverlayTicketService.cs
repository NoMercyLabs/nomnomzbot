// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Overlay;

/// <summary>
/// Issues and redeems short-lived, single-use overlay connection tickets (S035 item 3, U·B5/B7). OBS browser
/// sources cannot set custom WebSocket headers, so the long-lived <c>Channel.OverlayToken</c> can never ride
/// on the hub connection query string without leaking into proxy/access logs and browser history. Instead the
/// SDK exchanges the long-lived token for a ticket over a plain HTTP request (which CAN carry a header), and
/// only the ticket — opaque, unguessable, expires in seconds, and burns on first use — appears in the
/// <c>/hubs/overlay</c> query string.
/// </summary>
public interface IOverlayTicketService
{
    /// <summary>Mints a ticket bound to <paramref name="broadcasterId"/>. Never fails — the caller already
    /// validated the long-lived overlay token before calling this.</summary>
    string IssueTicket(Guid broadcasterId);

    /// <summary>Redeems (and burns) a ticket. Returns the bound broadcaster id, or <c>null</c> when the
    /// ticket is missing, unknown, expired, or already used.</summary>
    Guid? RedeemTicket(string? ticket);
}
