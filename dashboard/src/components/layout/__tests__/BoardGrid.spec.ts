// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
});

const panels = [
  { id: 'queue', label: 'Review queue' },
  { id: 'connections', label: 'Connections' },
  { id: 'happening', label: 'Happening now' },
] as const;

const Board = defineComponent({
  render: () =>
    h(BoardGrid, { panels }, () =>
      panels.map((panel) =>
        h(BoardCell, { panel: panel.id }, () =>
          h('p', { 'data-body': panel.id }, panel.label)))),
});

async function boardAt(width: number, path = '/'): Promise<{ router: Router; wrapper: VueWrapper }> {
  setWidth(width);

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { render: () => null } }],
  });

  await router.push(path);
  mounted = mount(Board, { global: { plugins: [router] } });
  await flushPromises();

  return { router, wrapper: mounted };
}

function visibleBodies(wrapper: VueWrapper): string[] {
  return wrapper
    .findAll('[data-body]')
    .filter((body) =>
      body.element.closest('[data-slot="board-cell"]')?.getAttribute('style')?.includes('display: none') !== true)
    .map((body) =>
      body.attributes('data-body') ?? '');
}

// Stoney 2026-10-13: "A scroll within a scroll is bad for mobile and expanding all full is not an option, we need
// something better for those pages".
describe('the board on a phone', () => {
  it('shows one panel at a time behind a tab per panel, the first one at the start', async () => {
    const { wrapper } = await boardAt(phoneWidth);

    expect(wrapper.findAll('[role="tab"]').map((tab) =>
      tab.text())).toEqual(['Review queue', 'Connections', 'Happening now']);

    expect(visibleBodies(wrapper)).toEqual(['queue']);
  });

  it('opens the panel named in the link, and a tab press moves to its panel as a history step', async () => {
    const { router, wrapper } = await boardAt(phoneWidth, '/?panel=happening');

    expect(visibleBodies(wrapper)).toEqual(['happening']);

    await router.push({ query: { panel: 'connections' } });
    await flushPromises();

    expect(visibleBodies(wrapper)).toEqual(['connections']);

    router.back();
    await flushPromises();

    expect(router.currentRoute.value.query.panel).toBe('happening');
  });
});

describe('the board from laptop width', () => {
  it('shows every panel side by side with no tabs', async () => {
    const { wrapper } = await boardAt(laptopWidth, '/?panel=happening');

    expect(wrapper.findAll('[role="tab"]')).toHaveLength(0);
    expect(visibleBodies(wrapper)).toEqual(['queue', 'connections', 'happening']);
  });
});
