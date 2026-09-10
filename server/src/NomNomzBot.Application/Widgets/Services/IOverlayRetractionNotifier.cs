// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// Abstraction that lets <c>OverlayModerationRetractionHandler</c> push a moderation retraction
/// (widgets-overlays.md §2a) to the overlay without taking a direct dependency on the API layer's
/// SignalR hub. Implemented by <c>OverlayRetractionNotifierAdapter</c> in the API layer; a no-op
/// <c>NullOverlayRetractionNotifier</c> stands in for host-less contexts (workers, tests).
/// </summary>
public interface IOverlayRetractionNotifier
{
    /// <summary>
    /// Pushes a retraction on the SAME overlay connection/group as the content it cancels, so it can
    /// never overtake it. <paramref name="authorUserId"/> is the platform-native author id — the same
    /// id a widget already attached to what it rendered — not the resolved local user id.
    /// </summary>
    Task RetractAsync(
        Guid broadcasterId,
        string? sourceMessageId,
        string? authorUserId,
        string reason,
        CancellationToken ct = default
    );
}
