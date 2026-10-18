// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type InjectionKey, shallowRef } from 'vue';

// One panel of a board: its id names it in the link (?panel=), its label names its phone tab.
// minWidth is the narrowest column the panel renders properly in (the page writes the reason beside the value).
// An optional panel has a show/hide switch in the page header and joins as a column only where every panel
// keeps its minimum; narrower, it stays in the page's drawer.
export type BoardPanel<Id extends string = string> = {
  id: Id;
  label: string;
  minWidth?: number;
  optional?: boolean;
};

// A panel that declares no minimum needs half of a laptop board: two of them side by side.
export const defaultPanelMinWidth = 520;

// The board's side padding (px-6 each side) and the gap between columns (gap-4).
export const boardPadding = 48;
export const boardGap = 16;

// The board width at which `columns` equal columns each keep `minimum`.
export function boardWidthFor(columns: number, minimum: number): number {
  return columns * minimum + (columns - 1) * boardGap + boardPadding;
}

// How many equal columns of at least `minimum` fit the board width, at most `limit`, at least one.
export function boardColumns(width: number, minimum: number, limit: number): number {
  const fitting = Math.floor((width - boardPadding + boardGap) / (minimum + boardGap));

  return Math.min(limit, Math.max(1, fitting));
}

// The switch of one optional panel, read by the page header.
export type BoardToggle = {
  id: string;
  label: string;
  shown: boolean;
  set: (shown: boolean) => void;
};

export const boardToggles = shallowRef<readonly BoardToggle[]>([]);

// A cell asks its board whether its panel shows: always from laptop width, only the open tab on a phone.
export const boardKey: InjectionKey<(panel: string) => boolean> = Symbol('board');
