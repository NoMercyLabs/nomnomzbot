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

import { describe, expect, it } from 'vitest';

import en from '@/i18n/locales/en.json';
import nl from '@/i18n/locales/nl.json';

const CRUMB_KEY = 'dashboard.settings.crumb';

// Every settings page source, as text.
const pages: Record<string, string> = import.meta.glob('../**/*.vue', {
  eager: true,
  query: '?raw',
  import: 'default',
});

// Every page words file under settings (each loads only with its own page).
const pageWords: Record<string, unknown> = import.meta.glob('../**/i18n/*.json', {
  eager: true,
  import: 'default',
});

function read(words: unknown, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>(
      (node, key) =>
        node && typeof node === 'object'
          ? (node as Record<string, unknown>)[key]
          : undefined,
      words,
    );
}

// A settings page opens on its own, so the words it can count on are the shared ones only.
// The crumb that every settings page shows must live there, never in one page's file.
describe('the shared settings crumb', () => {
  const users = Object.entries(pages)
    .filter(([, source]) => source.includes(`'${CRUMB_KEY}'`))
    .map(([path]) => path);

  it('is used by the settings pages', () => {
    expect(users.length).toBeGreaterThanOrEqual(11);
  });

  it('is in the always-loaded words for every language', () => {
    expect(typeof read(en, CRUMB_KEY)).toBe('string');
    expect(typeof read(nl, CRUMB_KEY)).toBe('string');
  });

  it('is not repeated in a page words file', () => {
    const copies = Object.entries(pageWords)
      .filter(([, words]) => read(words, CRUMB_KEY) !== undefined)
      .map(([path]) => path);

    expect(copies).toEqual([]);
  });
});
