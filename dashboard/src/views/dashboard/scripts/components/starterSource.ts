// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The code a new script starts with: it does nothing yet, and says where to begin.
export const STARTER_SOURCE: string = `// Your script starts here. It stays off until you switch it on.
// Pick a die size: the first word after the command, or 6.
const sides: number = Number(bot.args[0]) || 6;

// Pick a whole number from 1 to sides. Add nnz.api.chat.send(...) to post it in chat.
const roll: number = nnz.random.int(1, sides);
`
