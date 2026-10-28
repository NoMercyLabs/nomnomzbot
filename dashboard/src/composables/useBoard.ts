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

import { type MaybeComputedElementRef, useLocalStorage } from '@vueuse/core';
import {
  computed,
  type ComputedRef,
  effectScope,
  getCurrentScope,
  onScopeDispose,
  type Ref,
  toValue,
  watchEffect,
} from 'vue';

import {
  boardColumns,
  type BoardPanel,
  boardToggles,
  defaultPanelMinWidth,
} from '@/components/layout/board';

import { merge } from '@/lib/array';

import { useWidth } from './useLayout';

const baseColumns = 2;
const gridColumns = 12;

export type Board = {
  // Whether the board holds two columns of its panels at all; narrower, its panels stack.
  wide: ComputedRef<boolean>;
  // Whether the board is too narrow for two columns of its panels and shows one panel at a time.
  tabbed: ComputedRef<boolean>;
  // The columns the board holds, and the grid span (of 12) a panel takes in them.
  columns: ComputedRef<number>;
  span: ComputedRef<number>;
  // Whether the optional panel is switched on and sits as a column. On but not inline means: in the drawer.
  isShown: (id: string) => boolean;
  isInline: (id: string) => boolean;
};

function widest(panels: readonly BoardPanel[]): number {
  return Math.max(...panels.map((panel) =>
    panel.minWidth ?? defaultPanelMinWidth));
}

// The one place a board counts its columns: from its measured width and the minimum each panel declares.
// The columns are equal, so the widest minimum sets the column. The regular panels fill two columns; an
// optional panel adds a column only if every visible panel still keeps its minimum, else it stays in the drawer.
// An optional panel's switch (remembered per page in 'nnz.<page>.panel.<id>') shows in the page header.
export function useBoard(
  page: string,
  target: MaybeComputedElementRef,
  panels: () => readonly BoardPanel[],
) {
  const width = useWidth(target);
  const scope = getCurrentScope() ?? effectScope();
  const switches = new Map<string, Ref<boolean>>();

  function switchOf(id: string): Ref<boolean> {
    const existing = switches.get(id);

    if (existing)
      return existing;

    const created = scope.run(() =>
      useLocalStorage<boolean>(`nnz.${page}.panel.${id}`, true)) as Ref<boolean>;

    switches.set(id, created);

    return created;
  }

  const regular = computed(() =>
    panels().filter((panel) =>
      !panel.optional));
  const wanted = computed(() =>
    panels().filter((panel) =>
      panel.optional && switchOf(panel.id).value));
  const wide = computed(() =>
    boardColumns(width.value, widest(regular.value), baseColumns) >= baseColumns);
  const tabbed = computed(() =>
    regular.value.length > 1 && !wide.value);

  const inline = computed<readonly string[]>(() => {
    if (tabbed.value)
      return [];

    const accepted: BoardPanel[] = [];

    for (const panel of wanted.value) {
      const group = merge(regular.value, accepted, [panel]);
      const needed = baseColumns + accepted.length + 1;

      if (boardColumns(width.value, widest(group), gridColumns) < needed) break;

      accepted.push(panel);
    }

    return accepted.map((panel) =>
      panel.id);
  });

  const columns = computed(() =>
    Math.min(baseColumns, regular.value.length) + inline.value.length);
  const span = computed(() =>
    gridColumns / Math.max(columns.value, 1));

  watchEffect(() => {
    boardToggles.value = panels()
      .filter((panel) =>
        panel.optional)
      .map((panel) => ({
        id: panel.id,
        label: panel.label,
        shown: switchOf(panel.id).value,
        set: (shown: boolean): void => {
          switchOf(panel.id).value = shown;
        },
      }));
  });

  onScopeDispose(() => {
    boardToggles.value = [];
  });

  return {
    wide,
    tabbed,
    columns,
    span,
    isShown: (id: string): boolean =>
      switchOf(id).value,
    isInline: (id: string): boolean =>
      inline.value.includes(toValue(id)),
  } as const satisfies Board;
}
