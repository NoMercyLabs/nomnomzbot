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

import { describe, expect, it, vi } from 'vitest';
import { nextTick, ref } from 'vue';

import { useNumberField } from '../useNumberField';

function field(stored: number | undefined, most: number | 'none' = 10) {
  const ceiling = most === 'none'
    ? undefined
    : most;
  const held = ref(stored);
  const commit = vi.fn<(next: number) => void>();

  return {
    held,
    commit,
    ...useNumberField({
      stored: () => held.value,
      fewest: 1,
      most: () => ceiling,
      commit,
    }),
  };
}

describe('useNumberField', () => {
  it('shows the stored value and follows it when the server changes it', async () => {
    const box = field(5);

    expect(box.text.value).toBe('5');

    box.held.value = 7;
    await nextTick();

    expect(box.text.value).toBe('7');
  });

  it('holds a too-large entry at the most, shows it back and commits it', () => {
    const box = field(5);

    box.text.value = '99';
    box.save();

    expect(box.text.value).toBe('10');
    expect(box.commit).toHaveBeenCalledExactlyOnceWith(10);
  });

  it('lifts a too-small entry to the fewest and turns a fraction into a whole number', () => {
    const low = field(5);

    low.text.value = '-4';
    low.save();
    expect(low.commit).toHaveBeenCalledExactlyOnceWith(1);

    const fraction = field(5);

    fraction.text.value = '7.9';
    fraction.save();
    expect(fraction.commit).toHaveBeenCalledExactlyOnceWith(7);
  });

  it('falls back to the fewest for text that is not a number', () => {
    const box = field(5);

    box.text.value = 'abc';
    box.save();

    expect(box.text.value).toBe('1');
    expect(box.commit).toHaveBeenCalledExactlyOnceWith(1);
  });

  it('sends nothing when the entry equals the stored value', () => {
    const box = field(5);

    box.text.value = '5';
    box.save();

    expect(box.commit).not.toHaveBeenCalled();
  });

  it('has no ceiling until the server names one', () => {
    const box = field(5, 'none');

    box.text.value = '12345';
    box.save();

    expect(box.commit).toHaveBeenCalledExactlyOnceWith(12345);
  });
});
