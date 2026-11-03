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

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// The warning codes a test fire can return. They are codes, not prose: the dashboard words them in the
/// user's language.
/// </summary>
public static class EventResponseTestWarnings
{
    /// <summary>The event has no response, or the response is switched off: nothing was presented.</summary>
    public const string ResponseDisabled = "response_disabled";

    /// <summary>The response runs a pipeline but none is bound, or the bound one is missing or disabled.</summary>
    public const string NoPipeline = "no_pipeline";

    /// <summary>No overlay browser source is attached, so an alert has nowhere to show.</summary>
    public const string NoOverlaySource = "no_overlay_source";
}
