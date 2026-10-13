// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

type List = readonly unknown[]

type Merged<Lists extends readonly List[]> = Lists extends readonly [
  infer First extends List,
  ...infer Rest extends readonly List[],
]
  ? readonly [...First, ...Merged<Rest>]
  : readonly []

// Several lists as one new read-only list, in order. Tuples stay tuples, so `as const` input
// keeps every position typed (owner 2026-10-13: "turn [...a, ...b] into merge(a, b)").
export function merge<const Lists extends readonly List[]>(...lists: Lists): Merged<Lists> {
  return lists.flat() as unknown as Merged<Lists>
}

// Object.keys typed by the object's own keys. Only for objects whose type lists every key they
// hold, such as a `const` table; an object with extra runtime keys would lie about them.
export function keysOf<T extends object>(value: T): (keyof T)[] {
  return Object.keys(value) as (keyof T)[]
}
