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

using System.Text.Json;

namespace NomNomzBot.Application.DTOs.Twitch.EventSub;

/// <summary>
/// The transport-agnostic notification both the WebSocket receive loop and the webhook controller produce
/// from their wire frame (twitch-eventsub §4.1). The dispatcher dedupes, journals, and fans it out. The raw
/// <see cref="Event"/> object stays on <c>System.Text.Json</c> (the untrusted hot-path parser).
/// </summary>
public sealed record EventSubNotification
{
    /// <summary>Twitch's message-id — the dedupe key (UUIDv5-derives the journal <c>EventId</c>).</summary>
    public required string MessageId { get; init; }

    public required DateTimeOffset MessageTimestamp { get; init; }

    /// <summary>The EventSub topic, e.g. <c>channel.follow</c>.</summary>
    public required string SubscriptionType { get; init; }

    public required string SubscriptionVersion { get; init; }

    /// <summary>The resolved owning tenant (FK→Channels.Id).</summary>
    public required Guid BroadcasterId { get; init; }

    /// <summary>The raw broadcaster id from the condition/payload (kept for the journal payload).</summary>
    public required string TwitchBroadcasterUserId { get; init; }

    /// <summary>The raw event object exactly as Twitch sent it.</summary>
    public required JsonElement Event { get; init; }
}
