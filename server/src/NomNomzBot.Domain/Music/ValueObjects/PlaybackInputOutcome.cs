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

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>
/// What the playback director did with one <see cref="PlaybackInput"/>. A control surface reads it so it
/// never reports a change that did not happen.
/// </summary>
public enum PlaybackInputOutcome
{
    /// <summary>The director acted on the input (a state it handles, even when an engine then refused it).</summary>
    Applied = 0,

    /// <summary>The state has no row for the input, so nothing changed and no engine was commanded.</summary>
    Ignored,
}
