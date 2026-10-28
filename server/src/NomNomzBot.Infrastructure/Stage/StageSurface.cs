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

namespace NomNomzBot.Infrastructure.Stage;

/// <summary>
/// The names that tie the stage system surface together: the catalogue key of the widget, and the one widget
/// event type it listens for. The catalogue entry, the pipeline action, the payload registry and the Vue asset
/// all read these, so they cannot drift apart.
/// </summary>
public static class StageSurface
{
    /// <summary>The catalogue (natural) key of the channel's stage widget.</summary>
    public const string WidgetKey = "stage";

    /// <summary>The widget event type every stage effect is pushed as.</summary>
    public const string EventType = "stage_effect";
}
