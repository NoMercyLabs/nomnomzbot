// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

const frame: string = readFileSync(resolve(__dirname, '../frame.css'), 'utf8');

// jsdom has no layout, so the phone rule is read from the source: the block of the narrow-screen media query.
function phoneBlock(): string {
  const start: number = frame.indexOf('@media (width < 48rem)');

  return frame.slice(start, frame.indexOf('/* Status is a dot', start));
}

describe('phone hit targets', () => {
  it('gives tab triggers and page-bar segments at least 36px under 48rem', () => {
    const block: string = phoneBlock();

    expect(block).toMatch(/\[data-slot='tabs-trigger'\][^{]*\{[^}]*min-height: 36px/);
    expect(block).toMatch(/\[data-segment\][^{]*\{[^}]*min-height: 36px/);
  });

  it('lets the tab track grow with its triggers under 48rem', () => {
    expect(phoneBlock()).toMatch(/\[data-slot='tabs-list'\]\s*\{[^}]*height: auto/);
  });
});
