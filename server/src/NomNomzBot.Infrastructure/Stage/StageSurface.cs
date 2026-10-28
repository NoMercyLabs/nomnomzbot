// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
