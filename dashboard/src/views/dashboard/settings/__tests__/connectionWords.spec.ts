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

// The one source of the connection words is common.connection (ConnectionRow reads it). A page file that
// holds its own copy of Connected, Not connected or Needs attention drifts from it: "Disconnected" in one
// place and "Not connected" in another.
const WORDS = [
  en.common.connection.connected,
  en.common.connection.disconnected,
  en.common.connection.attention,
  'Disconnected',
] as const satisfies readonly string[];

// Page files outside the settings and catalog lane that still hold their own copy; each is a debt to pay down.
const STILL_OWN_COPY = [
  '/home/i18n/en.json',
  '/obs/i18n/en.json',
  '/stream/i18n/en.json',
  '/supporters/i18n/en.json',
  '/vts/i18n/en.json',
] as const satisfies readonly string[];

const pageWords: Record<string, unknown> = import.meta.glob('../../**/i18n/en.json', {
  eager: true,
  import: 'default',
});

function ownCopies(node: unknown, path: string): string[] {
  if (typeof node === 'string')
    return WORDS.includes(node) && /(connected|attention)$/i.test(path)
      ? [path]
      : [];

  if (node === null || typeof node !== 'object')
    return [];

  return Object.entries(node).flatMap(([key, child]) =>
    ownCopies(child, `${path}.${key}`));
}

describe('the connection words', () => {
  it('come from common.connection in every page file of the settings and the catalog', () => {
    const found = Object.entries(pageWords)
      .filter(([file]) => !STILL_OWN_COPY.some((debt) => file.endsWith(debt)))
      .flatMap(([file, words]) => ownCopies(words, file.replace('../../', '')));

    expect(found).toEqual([]);
  });

  it('keeps one tone per state on the ConnectionRow', () => {
    expect(en.common.connection.disconnected).toBe('Not connected');
  });
});
