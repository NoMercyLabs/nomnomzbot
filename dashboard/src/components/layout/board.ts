// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { InjectionKey } from 'vue';

// One panel of a board: its id names it in the link (?panel=), its label names its phone tab.
export type BoardPanel = {
  id: string;
  label: string;
};

// A cell asks its board whether its panel shows: always from laptop width, only the open tab on a phone.
export const boardKey: InjectionKey<(panel: string) => boolean> = Symbol('board');
