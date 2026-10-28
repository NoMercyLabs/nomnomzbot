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

/// <summary>The optional payload fields a stage preset reads. A field a preset does not use is never sent.</summary>
[Flags]
public enum StagePresetFields
{
    None = 0,

    /// <summary>The words (or emoji) the preset shows.</summary>
    Text = 1,

    /// <summary>How big and fast the effect is.</summary>
    Intensity = 2,

    /// <summary>The main color of the effect.</summary>
    Color = 4,
}
