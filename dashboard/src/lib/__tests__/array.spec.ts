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

import { describe, expect, expectTypeOf, it } from 'vitest';

import { evenSteps, keysOf, lookup, merge, oneOf } from '../array';

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
    expectTypeOf(keys).toEqualTypeOf<['sm' | 'md' | 'lg', ...('sm' | 'md' | 'lg')[]]>();
  });
});

describe('oneOf', () => {
  it('returns the list item that equals the value, typed as the list items', () => {
    const tabs = keysOf({
      all: 1,
      on: 2,
    } as const);

    const found = oneOf(tabs, 'on');

    expect(found).toBe('on');
    expectTypeOf(found).toEqualTypeOf<'all' | 'on' | undefined>();
  });

  it('returns undefined for a value outside the list or no value', () => {
    const tabs = ['all', 'on'] as const;

    expect(oneOf(tabs, 'off')).toBeUndefined();
    expect(oneOf(tabs, undefined)).toBeUndefined();
  });
});

describe('lookup', () => {
  const tones = {
    done: 'ok',
    failed: 'bad',
  } as const;

  it('returns the table entry for a known key, typed as the table values', () => {
    const tone = lookup(tones, 'failed');

    expect(tone).toBe('bad');
    expectTypeOf(tone).toEqualTypeOf<'ok' | 'bad' | undefined>();
  });

  it('returns undefined for an unknown key, no key, or a prototype name', () => {
    expect(lookup(tones, 'other')).toBeUndefined();
    expect(lookup(tones, undefined)).toBeUndefined();
    expect(lookup(tones, 'toString')).toBeUndefined();
  });
});

describe('evenSteps', () => {
  it('keeps every position when there are few enough', () => {
    expect(evenSteps(5, 8)).toEqual([0, 1, 2, 3, 4]);
  });

  it('thins 90 positions to at most 8 evenly spaced ones', () => {
    const ticks = evenSteps(90, 8);

    expect(ticks.length).toBeLessThanOrEqual(8);
    expect(ticks[0]).toBe(0);
    const gaps = ticks.slice(1).map((tick, at) => tick - (ticks[at] ?? 0));

    expect(new Set(gaps).size).toBe(1);
  });

  it('gives nothing for an empty list', () => {
    expect(evenSteps(0, 8)).toEqual([]);
  });
});
