// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// One part of a query key: a name, an id, a flag, or a filter object.
export type KeyPart = string | number | boolean | null | undefined | object;

// A query key as the cache holds it.
export type KeyParts = KeyPart[];

// merge takes lists of any item type, so its input must accept everything.
// eslint-disable-next-line no-restricted-syntax -- merge takes lists of any item type
type List = readonly unknown[];

type Merged<Lists extends readonly List[]> = Lists extends readonly [
  infer First extends List,
  ...infer Rest extends readonly List[],
]
  ? readonly [...First, ...Merged<Rest>]
  : readonly [];

// Several lists as one new read-only list, in order. Tuples stay tuples, so `as const` input
// keeps every position typed (owner 2026-10-13: "turn [...a, ...b] into merge(a, b)").
export function merge<const Lists extends readonly List[]>(...lists: Lists): Merged<Lists> {
// eslint-disable-next-line no-restricted-syntax -- flat() widens the tuple the Merged type restores
  return lists.flat() as unknown as Merged<Lists>;
}

// Object.keys typed by the object's own keys. Only for objects whose type lists every key they
// hold, such as a `const` table; an object with extra runtime keys would lie about them.
export function keysOf<T extends object>(value: T): (keyof T)[] {
  return Object.keys(value) as (keyof T)[];
}

// A list cut into runs of at most `size`, in order.
export function chunk<T>(items: readonly T[], size: number): T[][] {
  const runs: T[][] = [];

  for (let start = 0; start < items.length; start += size) {
    runs.push(items.slice(start, start + size));
  }

  return runs;
}

// A copy of the list with the item at `from` moved to `to`; an index outside the list leaves it as it was.
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const moved = [...items];

  if (from < 0 || from >= moved.length || to < 0 || to >= moved.length)
    return moved;

  const removed = moved.splice(from, 1);

  moved.splice(to, 0, ...removed);

  return moved;
}
