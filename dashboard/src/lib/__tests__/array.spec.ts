// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, expectTypeOf, it } from 'vitest';

import { keysOf, merge } from '../array';

describe('merge', () => {
  it('joins the lists in order and keeps each position typed, read-only', () => {
    const numbers = [1, 2] as const;
    const words = ['a'] as const;

    const merged = merge(numbers, words, [] as const);

    expect(merged).toEqual([1, 2, 'a']);
    expectTypeOf(merged).toEqualTypeOf<readonly [1, 2, 'a']>();
  });

  it('joins plain arrays into one read-only array of their element types', () => {
    const ids: string[] = ['x'];
    const counts: number[] = [3, 4];

    const merged = merge(ids, counts);

    expect(merged).toEqual(['x', 3, 4]);
    expectTypeOf(merged).toEqualTypeOf<readonly (string | number)[]>();
  });

  it('leaves its inputs untouched', () => {
    const first: number[] = [1];

    merge(first, [2]);

    expect(first).toEqual([1]);
  });
});

describe('keysOf', () => {
  it('returns the own keys in insertion order, typed as the object keys', () => {
    const sizes = {
      sm: 1,
      md: 2,
      lg: 3,
    } as const;

    const keys = keysOf(sizes);

    expect(keys).toEqual(['sm', 'md', 'lg']);
    expectTypeOf(keys).toEqualTypeOf<('sm' | 'md' | 'lg')[]>();
  });
});
