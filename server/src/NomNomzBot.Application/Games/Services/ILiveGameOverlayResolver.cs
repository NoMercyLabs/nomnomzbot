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

namespace NomNomzBot.Application.Games.Services;

/// <summary>
/// Resolves a manifest's <c>OverlayWidgetKey</c> to the channel's installed, enabled overlay widget —
/// the widgets-domain seam the engine pushes frames through (D5). Null when the channel has not installed
/// the game's widget: the round still runs, only the overlay stays dark.
/// </summary>
public interface ILiveGameOverlayResolver
{
    Task<Guid?> ResolveAsync(
        Guid broadcasterId,
        string overlayWidgetKey,
        CancellationToken ct = default
    );
}
