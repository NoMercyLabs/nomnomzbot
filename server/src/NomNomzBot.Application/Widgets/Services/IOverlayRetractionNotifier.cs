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
