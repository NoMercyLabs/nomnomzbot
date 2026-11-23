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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;

namespace NomNomzBot.Application.Obs.Services;

/// <summary>
/// Puts the overlays a streamer picked into OBS in one call. A nested scene holds one browser source per picked
/// overlay, named and set up from the overlay's own OBS setup values; the nested scene is then added to the picked
/// scene. Nothing is installed that was not picked, and running it again updates instead of duplicating.
/// </summary>
public interface IObsOverlayInstallService
{
    /// <summary>The name of the nested scene that holds the overlay sources.</summary>
    const string NestedSceneName = "NomNomzBot overlays";

    /// <summary>
    /// Installs the overlays named by <paramref name="overlayKeys"/>. <paramref name="overlayOrigin"/> is the public
    /// origin the overlay addresses are built on. Fails with the transport's code (<c>OBS_NOT_CONNECTED</c>,
    /// <c>OBS_BRIDGE_OFFLINE</c>) when OBS cannot be reached; an overlay OBS refuses is reported in its own entry.
    /// </summary>
    Task<Result<ObsOverlayInstallResult>> InstallAsync(
        Guid broadcasterId,
        string sceneName,
        IReadOnlyList<string> overlayKeys,
        string overlayOrigin,
        CancellationToken ct = default
    );
}
