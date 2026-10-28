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

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

const frame: string = readFileSync(resolve(__dirname, '../frame.css'), 'utf8');

// jsdom has no layout, so the phone rule is read from the source: the block of the narrow-screen media query.
function phoneBlock(): string {
  const start = frame.indexOf('@media (width < 48rem)');

  return frame.slice(start, frame.indexOf('/* Status is a dot', start));
}

describe('phone hit targets', () => {
  it('gives tab triggers and page-bar segments at least 36px under 48rem', () => {
    const block = phoneBlock();

    expect(block).toMatch(/\[data-slot='tabs-trigger'\][^{]*\{[^}]*min-height: 36px/);
    expect(block).toMatch(/\[data-segment\][^{]*\{[^}]*min-height: 36px/);
  });

  // The People sort pills sit in a panel, not the page bar, and stayed at the 28px --seg-h on a phone.
  it('gives a segment inside a panel at least 36px under 48rem too', () => {
    expect(phoneBlock()).toMatch(/(^|,)\s*\[data-segment\]\s*(,|\{)[^}]*min-height: 36px/m);
  });

  it('lets the tab track grow with its triggers under 48rem', () => {
    expect(phoneBlock()).toMatch(/\[data-slot='tabs-list'\]\s*\{[^}]*height: auto/);
  });
});
