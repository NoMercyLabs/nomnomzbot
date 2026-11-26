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

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// The build an overlay page runs: its SDK, its theme and its widget bundle. The host page bakes it in, the SDK
/// reports it on every join, and the hub tells a page whose build is older to reload. That is how an OBS source
/// that stays open across a deploy picks up the new code.
/// </summary>
public static class OverlayPageVersion
{
    public static string Of(string bundleContentHash) =>
        $"{OverlaySdkController.Version}.{OverlayThemeController.Version}.{bundleContentHash}";
}
