// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A board cell stretches its panel to the screen height. Below md an empty Home panel keeps only the height
// of its own message, so a phone never shows a big blank card (Stoney, 2026-10-21).
export const SHRINK_WHEN_EMPTY: string = 'max-md:data-[state=empty]:flex-none!';
