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

const frame = readFileSync(resolve(__dirname, '../frame.css'), 'utf8');

// jsdom has no layout, so the rule is read from the source: the block whose selector list holds the selector.
function blockFor(selector: string): string {
  const pattern = new RegExp(`(^|\\n)([^{}/]*${selector}[^{}]*)\\{([^}]*)\\}`, 'g');
  const matches = [...frame.matchAll(pattern)];

  return matches.map((match) => `${match[2]}{${match[3]}}`).join('\n');
}

// Stoney 2026-10-20: "this still has the wrong switch toggle" - Density in a panel showed no track. A segmented
// control in a panel wears the page tab track, so the tab strip and the segmented control cannot drift apart.
describe('the segmented control track', () => {
  it('shares the tab-list track rule instead of a page-bar-only copy', () => {
    const track = blockFor(String.raw`\[data-slot='tabs-list'\]`);

    expect(track).toMatch(/\[data-segmented\]\s*\{[^}]*padding: 3px/);
    expect(track).not.toContain('page-bar');
    expect(track).toContain('padding: 3px');
    expect(track).toContain('gap: 2px');
  });

  it('shares the tab trigger rule and its height with every segment', () => {
    const trigger = blockFor(String.raw`\[data-slot='tabs-trigger'\]`);

    expect(trigger).toMatch(/\[data-segment\]\s*\{/);
    expect(trigger).not.toContain('page-bar');
    expect(trigger).toContain('height: var(--tab-h)');
  });

  it('keeps no separate in-panel padding, gap or height for a segmented control', () => {
    expect(frame).not.toContain('--seg-h);');
    expect(frame).not.toMatch(/\n\[data-segmented\] \{[^}]*padding: 2px/);
  });

  it('draws a ring from the foreground so the track shows on a page and on a muted row', () => {
    const track = blockFor(String.raw`\[data-segmented\]`);

    expect(track).toMatch(/box-shadow: inset 0 0 0 1px color-mix\(in oklch, var\(--foreground\)/);
  });
});
