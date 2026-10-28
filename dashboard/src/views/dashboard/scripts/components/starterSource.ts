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

// The code a new script starts with: it does nothing yet, and says where to begin.
export const STARTER_SOURCE: string = `// Your script starts here. It stays off until you switch it on.
// Pick a die size: the first word after the command, or 6.
const sides: number = Number(bot.args[0]) || 6;

// Pick a whole number from 1 to sides. Add nnz.api.chat.send(...) to post it in chat.
const roll: number = nnz.random.int(1, sides);
`;
