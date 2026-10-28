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

import { useWordsFor } from '../useWordsFor';

const words = new Map([
  ['status.dead_letter_x', 'Gave up'],
  ['status.failed', 'Failed'],
]);

vi.mock('vue-i18n', () => ({
  useI18n: () => ({
    te: (key: string) => words.has(key),
    t: (key: string) => words.get(key),
  }),
}));

describe('useWordsFor', () => {
  it('turns a server value into its resource key and reads the words', () => {
    const wordsFor = useWordsFor('status');

    expect(wordsFor('Failed')).toBe('Failed');
    expect(wordsFor('Dead Letter!x')).toBe('Gave up');
  });

  it('shows a value without words as the server sent it', () => {
    expect(useWordsFor('status')('Brand New State')).toBe('Brand New State');
  });
});
