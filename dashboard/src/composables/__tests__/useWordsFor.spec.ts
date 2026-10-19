// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
