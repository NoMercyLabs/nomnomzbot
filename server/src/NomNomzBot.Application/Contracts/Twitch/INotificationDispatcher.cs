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
    ///   <item>Fan out to the strongly-typed per-topic domain event(s) via the matching
    ///   <see cref="IEventSubEventTranslator"/> (§3.7), published on <c>IEventBus</c> — new path only.</item>
    ///   <item>Emit <c>EventSubNotificationJournaledEvent</c> (with <c>WasDuplicate</c>).</item>
    /// </list>
    /// Returns the journaled event id + position (or the duplicate signal).
    /// </summary>
    Task<Result<NotificationDispatchResult>> DispatchAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    );
}
