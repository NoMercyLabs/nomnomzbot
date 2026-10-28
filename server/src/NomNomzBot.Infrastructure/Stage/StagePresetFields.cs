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
