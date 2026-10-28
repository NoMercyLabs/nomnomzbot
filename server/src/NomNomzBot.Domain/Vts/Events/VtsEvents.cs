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

namespace NomNomzBot.Domain.Vts.Events;

/// <summary>
/// A VTube Studio event arrived from the channel's VTS instance (vtube-studio.md §4) — via the
/// direct socket or the bridge. Feeds the <c>vts_event</c> trigger surface.
/// </summary>
public sealed class VtsEventReceived : DomainEventBase
{
    /// <summary>The VTS event type, e.g. <c>ModelLoadedEvent</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>The raw VTS event <c>data</c> JSON (may be empty).</summary>
    public required string PayloadJson { get; init; }
}

/// <summary>
/// The channel's direct VTube Studio connection changed state (<c>connected</c> or <c>error</c>) —
/// feeds the dashboard's live VTS status chip.
/// </summary>
public sealed class VtsStatusChangedEvent : DomainEventBase
{
    /// <summary>The new connection status, as stored on the connection row.</summary>
    public required string Status { get; init; }
}
