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

using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Enums;

namespace NomNomzBot.Domain.Twitch.Events;

/// <summary>
/// The transport dropped and a reconnect is in progress (backoff scheduled) — twitch-eventsub §2. A
/// diagnostic + dashboard "degraded" signal. The inherited <c>BroadcasterId</c> is the owning channel.
/// </summary>
public sealed class EventSubDisconnectedEvent : DomainEventBase
{
    /// <summary>The connection type EventSub uses: WebSocket, Conduit or Webhook.</summary>
    public required EventSubTransportKind Transport { get; init; }

    /// <summary>The WebSocket session id that dropped; null for the conduit transport.</summary>
    public string? SessionId { get; init; }

    /// <summary>The close code / exception summary (scrubbed of tokens and PII).</summary>
    public required string Reason { get; init; }

    /// <summary>How long until the next reconnect attempt (the current backoff delay).</summary>
    public required TimeSpan NextRetryIn { get; init; }
}
