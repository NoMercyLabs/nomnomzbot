// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { ref } from 'vue';

import { useShowMore } from '../useShowMore';

function numbers(count: number): number[] {
  return Array.from({ length: count }, (_, index) => index);
}

describe('useShowMore', () => {
  it('shows only the first step and reports what is left', () => {
    const list = useShowMore(() => numbers(25), 10);

    expect(list.visible.value).toEqual(numbers(10));
    expect(list.remaining.value).toBe(15);
    expect(list.hasMore.value).toBe(true);
  });

  it('grows by one step per call and stops at the end of the list', () => {
    const list = useShowMore(() => numbers(25), 10);

    list.more();
    expect(list.visible.value).toHaveLength(20);

    list.more();
    expect(list.visible.value).toHaveLength(25);
    expect(list.remaining.value).toBe(0);
    expect(list.hasMore.value).toBe(false);
  });

  it('follows a list that grows live without folding it back', () => {
    const source = ref<number[]>(numbers(12));
    const list = useShowMore(() => source.value, 10);

    list.more();
    source.value = numbers(30);

    expect(list.visible.value).toHaveLength(20);
    expect(list.hasMore.value).toBe(true);
  });
});
