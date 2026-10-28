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

namespace NomNomzBot.Application.Overlays.Services;

/// <summary>
/// The generic overlay event feed: pushes EVERY domain event (by canonical type + raw JSON payload) to a channel's
/// connected overlay browser-sources, so a custom overlay is just a web app that connects and listens — it filters
/// client-side for the events it cares about (chat, alerts, now-playing, custom, …). Implemented in the API layer
/// (<c>OverlayEventFeedAdapter</c>) over the SignalR overlay hub; a no-op fallback runs where no hub is hosted.
/// </summary>
public interface IOverlayEventFeed
{
    /// <summary>
    /// Broadcasts one event to every overlay connection for <paramref name="broadcasterId"/>. <paramref name="eventType"/>
    /// is the canonical event name the overlay filters on; <paramref name="payloadJson"/> is the event's data as a raw
    /// JSON string (the overlay parses it). Best-effort — a delivery failure never blocks the emitting flow.
    /// </summary>
    Task BroadcastEventAsync(
        Guid broadcasterId,
        string eventType,
        string payloadJson,
        CancellationToken ct = default
    );
}
