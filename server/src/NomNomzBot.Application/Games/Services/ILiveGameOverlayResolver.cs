// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
