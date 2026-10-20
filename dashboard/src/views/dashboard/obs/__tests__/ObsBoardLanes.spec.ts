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

const root = resolve(__dirname, '../../../..');
const css = readFileSync(resolve(root, 'styles/main.css'), 'utf8');
const view = readFileSync(resolve(__dirname, '../ObsView.vue'), 'utf8');

// The block of the board-lanes utility, up to its closing brace at column zero.
const utility = /@utility board-lanes \{([\s\S]*?)\n\}/.exec(css)?.[1] ?? '';
const supports = /@supports \(display: grid-lanes\) \{([\s\S]*?)\}/.exec(utility)?.[1] ?? '';
const outside = utility.replace(/@supports[\s\S]*$/, '');

describe('the OBS board packs its panels with CSS grid lanes', () => {
  it('turns grid-lanes on only inside @supports', () => {
    expect(supports).toContain('display: grid-lanes');
    expect(outside).not.toContain('grid-lanes');
  });

  it('keeps the plain grid outside @supports as the fallback', () => {
    expect(outside).toContain('display: grid');
  });

  it('puts the utility on the live board and the overlays board, not the plain grid class', () => {
    const boards = view.match(/class="[^"]*board-lanes[^"]*"/g) ?? [];

    expect(boards).toHaveLength(2);
    expect(view).toMatch(/data-obs-board\s+class="board-lanes /);
  });
});
