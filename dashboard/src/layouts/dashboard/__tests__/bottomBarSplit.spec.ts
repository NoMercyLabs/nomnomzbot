// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { defineComponent } from 'vue';
import { describe, expect, it } from 'vitest';

import { splitBottomBar } from '../bottomBarSplit';
import { settingsNav } from '../settingsNavigation';
import type { NavEntry } from '../navigation';

const icon = defineComponent({ render: () => null });

function entry(route: string, bottom = false) {
  return {
    route,
    label: route,
    icon,
    bottom,
  } as const satisfies NavEntry;
}

const routes = (list: NavEntry[]): string[] => list.map((item) => item.route);

describe('splitBottomBar', () => {
  it('puts every page in the bar when they fit, so nothing hides behind More', () => {
    const { bar, sheet } = splitBottomBar([entry('economy'), entry('viewerChat')]);

    expect(routes(bar)).toEqual(['economy', 'viewerChat']);
    expect(sheet).toEqual([]);
  });

  it('keeps five pages in the bar, because a sixth slot for More is not needed', () => {
    const list = ['a', 'b', 'c', 'd', 'e'].map((route) => entry(route));
    const { bar, sheet } = splitBottomBar(list);

    expect(routes(bar)).toEqual(['a', 'b', 'c', 'd', 'e']);
    expect(sheet).toEqual([]);
  });

  it('fills four slots with the bottom pages first, then the next pages, and sends the rest to More', () => {
    const list = [entry('a'), entry('b', true), entry('c'), entry('d', true), entry('e'), entry('f')];
    const { bar, sheet } = splitBottomBar(list);

    expect(routes(bar)).toEqual(['b', 'd', 'a', 'c']);
    expect(routes(sheet)).toEqual(['e', 'f']);
  });
});

// Stoney 2026-10-21 (mobile ledger ST-08): the settings bar showed three entries and More.
describe('the settings bottom bar', () => {
  it('keeps the way back and the three pages a person opens most, and sends the rest to More', () => {
    const all = settingsNav.flatMap((group) => [...group.entries]);
    const { bar, sheet } = splitBottomBar(all);

    expect(routes(bar)).toEqual([
      'dashboard-home',
      'dashboard-settings',
      'dashboard-settings-bot-basics',
      'dashboard-settings-integrations',
    ]);

    expect(sheet).toHaveLength(all.length - 4);
  });
});
