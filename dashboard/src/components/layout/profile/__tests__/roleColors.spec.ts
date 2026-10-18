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
