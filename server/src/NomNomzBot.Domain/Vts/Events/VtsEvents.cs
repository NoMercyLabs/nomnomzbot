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
