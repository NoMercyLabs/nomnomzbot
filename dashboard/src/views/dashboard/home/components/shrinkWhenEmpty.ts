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

// A board cell stretches its panel to the screen height. Below md an empty Home panel keeps only the height
// of its own message, so a phone never shows a big blank card (Stoney, 2026-10-21).
export const SHRINK_WHEN_EMPTY: string = 'max-md:data-[state=empty]:flex-none!';
