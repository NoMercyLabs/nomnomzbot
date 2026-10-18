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

// The keys of a table as a non-empty tuple typed by the table's own keys. Only for objects whose
// type lists every key they hold, such as a `const` table; an object with extra runtime keys would
// lie about them. The one Object.keys cast of the dashboard lives here.
export function keysOf<const T extends object>(
  value: T,
): [Extract<keyof T, string>, ...Extract<keyof T, string>[]] {
  return Object.keys(value) as [Extract<keyof T, string>, ...Extract<keyof T, string>[]];
}

// The item of `list` equal to `value`, or undefined. It narrows a plain string (a route query, a
// prop, a tab name) to the known set without a cast: the result comes out of the list itself.
export function oneOf<const Item extends string>(
  list: readonly Item[],
  value: string | undefined,
): Item | undefined {
  return list.find((item) => item === value);
}

// The entry of a constant table for a key that arrives as a plain string (a status, an event type, a
// key name), or undefined when the table has no such key. The key is narrowed through the table's own
// key list, so no `as keyof` cast is needed and a name like "toString" never reaches the prototype.
export function lookup<const T extends object>(
  table: T,
  key: string | undefined,
): T[Extract<keyof T, string>] | undefined {
  const known = oneOf(keysOf(table), key);

  return known === undefined
    ? undefined
    : table[known];
}

// A list cut into runs of at most `size`, in order.
export function chunk<T>(items: readonly T[], size: number): T[][] {
  const runs: T[][] = [];

  for (let start = 0; start < items.length; start += size) {
    runs.push(items.slice(start, start + size));
  }

  return runs;
}

// The positions to label on an axis of `count` slots: every `step`-th one from the first, at most `most` of them.
export function evenSteps(count: number, most: number): number[] {
  const step = Math.max(1, Math.ceil(count / most));

  const slots = Math.ceil(count / step);

  return Array.from(Array(slots).keys(), (at: number) =>
    at * step);
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
