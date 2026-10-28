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

import { computed, type ComputedRef } from 'vue';
import { useRoute, useRouter } from 'vue-router';

export type SortDirection = 'asc' | 'desc';

export type TableSort<Column extends string> = {
  column: ComputedRef<Column>;
  direction: ComputedRef<SortDirection>;
  // Pressing the open column flips its direction; pressing another column opens it in its first direction.
  press: (column: Column) => void;
  // A new list in the open order. Numbers and text both sort; equal values keep their order.
  sorted: <Row>(rows: readonly Row[], read: (row: Row, column: Column) => number | string) => Row[];
};

type SortOptions<Column extends string> = {
  // The order a page opens in, with no query.
  fallback: { column: Column; direction: SortDirection };
  // Columns whose first press sorts A to Z (text). Every other column opens largest first.
  ascending?: readonly Column[];
};

// The sort of a table lives in the route query (?sort=length&dir=asc), so a link keeps it and Back undoes it. A
// missing or unknown value falls back to the order the page opens in; the query stays clean until a person presses.
export function useTableSort<const Column extends string>(
  columns: readonly [Column, ...Column[]],
  options: SortOptions<Column>,
) {
  const route = useRoute();
  const router = useRouter();

  const column = computed<Column>(() =>
    columns.find((known) => known === route.query.sort) ?? options.fallback.column);

  const direction = computed<SortDirection>(() => {
    if (route.query.sort === undefined)
      return options.fallback.direction;

    return route.query.dir === 'asc'
      ? 'asc'
      : 'desc';
  });

  function press(pressed: Column): void {
    const flipped: SortDirection = direction.value === 'asc'
      ? 'desc'
      : 'asc';
    const first: SortDirection = options.ascending?.includes(pressed)
      ? 'asc'
      : 'desc';
    const next = pressed === column.value
      ? flipped
      : first;

    void router.push({ query: {
      ...route.query,
      sort: pressed,
      dir: next,
    } });
  }

  function sorted<Row>(
    rows: readonly Row[],
    read: (row: Row, column: Column) => number | string,
  ): Row[] {
    const sign = direction.value === 'asc'
      ? 1
      : -1;
    const open = column.value;

    return [...rows].sort((a, b) => {
      const left = read(a, open);
      const right = read(b, open);

      if (typeof left === 'number' && typeof right === 'number')
        return sign * (left - right);

      return sign * String(left).localeCompare(String(right));
    });
  }

  return {
    column,
    direction,
    press,
    sorted,
  } as const satisfies TableSort<Column>;
}
