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

// The narrowest column (px) each Music panel renders properly in, measured from its real content.
// The board is made of equal columns, so the widest value sets how wide a column must be.
export const musicMinimums = {
  // The transport row is 320px (five buttons and their gaps) inside 16px padding on each side.
  now: 360,
  // A queue row keeps the number, the cover and the drag handle (148px) and still shows about 14 title characters.
  queue: 440,
  // History and thanks are plain lists of one line per entry.
  history: 360,
  thanks: 360,
} as const satisfies Record<string, number>;
