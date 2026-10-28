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
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

import { roleIcons } from '../profileRoles';

const theme = readFileSync(join(__dirname, '../../../../styles/main.css'), 'utf8');

// Owner 2026-10-18: moderator, subscriber and artist badges had a class with no color behind it.
describe('role colors', () => {
  it('backs every role color class with a theme color', () => {
    const missing = Object.values(roleIcons)
      .map((role) => role.color.replace('text-', '--color-'))
      .filter((token) => !theme.includes(`${token}:`));

    expect(missing).toEqual([]);
  });
});
