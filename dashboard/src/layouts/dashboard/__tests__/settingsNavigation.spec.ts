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

import { settingsNav } from '../settingsNavigation';

describe('the settings navigation', () => {
  // An icon is how a person finds a page again in a list of 28; two pages with one icon read as one place.
  it('gives every settings destination its own icon', () => {
    const owners = new Map<unknown, string>();
    const shared: string[] = [];

    for (const group of settingsNav) {
      for (const entry of group.entries) {
        const owner = owners.get(entry.icon);

        if (owner !== undefined) {
          shared.push(`${entry.label} shares its icon with ${owner}`);
        } else {
          owners.set(entry.icon, entry.label);
        }
      }
    }

    expect(shared).toEqual([]);
  });
});
