// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Twitch.EventSub;

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The single place a raw EventSub notification becomes a journaled, deduped, fanned-out event
/// (twitch-eventsub §3.4). Called by BOTH transports (the WebSocket receive loop and the SaaS webhook
/// controller) so dedupe/journal logic is not duplicated.
/// </summary>
public interface INotificationDispatcher
{
    /// <summary>
    /// Idempotently journals + fans out one notification:
    /// <list type="number">
    ///   <item>Journal via <c>IEventJournal.AppendAsync</c> — the append is idempotent on the message-id-derived
    ///   <c>EventId</c> (UUIDv5), so a redelivery returns the existing row and consumes no new position.</item>
    ///   <item>Map to the matching per-topic generic envelope and publish on <c>IEventBus</c>.</item>
    ///   <item>Emit <c>EventSubNotificationJournaledEvent</c> (with <c>WasDuplicate</c>).</item>
    /// </list>
    /// Returns the journaled event id + position (or the duplicate signal).
    /// </summary>
    Task<Result<NotificationDispatchResult>> DispatchAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    );
}
