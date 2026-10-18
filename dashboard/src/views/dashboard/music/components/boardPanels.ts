// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The narrowest column (px) each Music panel renders properly in, measured from its real content.
// The board is made of equal columns, so the widest value sets how wide a column must be.
export const musicMinimums = {
  // The transport row is 320px (five buttons and their gaps) inside 16px padding on each side.
  now: 360,
  // A queue row keeps the number, the cover and the drag handle (148px) and still shows about 14 title characters.
  queue: 440,
  // The rules form keeps its seconds input and its toggle rows on one line each.
  rules: 420,
  // History and thanks are plain lists of one line per entry.
  history: 360,
  thanks: 360,
} as const satisfies Record<string, number>;
