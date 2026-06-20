// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    public required EventSubTransportKind Transport { get; init; }

    /// <summary>The WebSocket session id that dropped; null for the conduit transport.</summary>
    public string? SessionId { get; init; }

    /// <summary>The close code / exception summary (scrubbed of tokens and PII).</summary>
    public required string Reason { get; init; }

    /// <summary>How long until the next reconnect attempt (the current backoff delay).</summary>
    public required TimeSpan NextRetryIn { get; init; }
}
