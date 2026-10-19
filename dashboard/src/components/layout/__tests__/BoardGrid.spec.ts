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
          h('p', { 'data-body': panel.id }, panel.label)),
      ),
    ),
});

async function boardAt(
  width: number,
  path = '/',
): Promise<{ router: Router; wrapper: VueWrapper }> {
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
    .filter(
      (body) =>
        body.element
          .closest('[data-slot="board-cell"]')
          ?.getAttribute('style')
          ?.includes('display: none') !== true,
    )
    .map((body) => body.attributes('data-body') ?? '');
}

// Stoney 2026-10-13: "A scroll within a scroll is bad for mobile and expanding all full is not an option, we need
// something better for those pages".
describe('the board on a phone', () => {
  it('shows one panel at a time behind a tab per panel, the first one at the start', async () => {
    const { wrapper } = await boardAt(phoneWidth);

    expect(wrapper.findAll('[role="tab"]').map((tab) => tab.text())).toEqual([
      'Review queue',
      'Connections',
      'Happening now',
    ]);

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

// Stoney 2026-10-14, a shot of Music at 1160 px with the rules squeezed into a strip: "constrained layouts need to
// be considered for". The sidebar takes its share, so the board decides by its own width, not the window's.
describe('a board squeezed by the sidebar', () => {
  const RealObserver = globalThis.ResizeObserver;

  afterEach(() => {
    if (RealObserver) {
      globalThis.ResizeObserver = RealObserver;
    } else {
      Reflect.deleteProperty(globalThis, 'ResizeObserver');
    }
  });

  it('shows one panel at a time when the board itself is too narrow for its columns', async () => {
    globalThis.ResizeObserver = class {
      constructor(private readonly report: ResizeObserverCallback) {}

      observe(target: Element): void {
        const entry = {
          target,
          contentRect: { width: 900, height: 800 },
          contentBoxSize: [{ inlineSize: 900, blockSize: 800 }],
        };

        this.report([entry as unknown as ResizeObserverEntry], this as unknown as ResizeObserver);
      }

      unobserve(): void {}

      disconnect(): void {}
    } as unknown as typeof ResizeObserver;

    const { wrapper } = await boardAt(laptopWidth);

    expect(wrapper.findAll('[role="tab"]')).toHaveLength(3);
    expect(visibleBodies(wrapper)).toEqual(['queue']);
  });
});

describe('the board from laptop width', () => {
  it('shows every panel side by side with no tabs', async () => {
    const { wrapper } = await boardAt(laptopWidth, '/?panel=happening');

    expect(wrapper.findAll('[role="tab"]')).toHaveLength(0);
    expect(visibleBodies(wrapper)).toEqual(['queue', 'connections', 'happening']);
  });
});

// A-tts defect 7: the panel tabs of a phone board sat 9 px left of the page tabs above them. The page header bar
// pads its row by 1.5rem (px-6), so the tab strip and the panel under it use the same inset.
describe('the tab strip of a tabbed board', () => {
  it('has the same side inset as the page header bar, for the strip and the panel under it', async () => {
    const { wrapper } = await boardAt(phoneWidth);
    const strip = wrapper.get('[data-slot="board-grid"] > [data-slot="tabs"]');
    const body = wrapper.get('[data-slot="board-grid"] > div');

    expect(strip.classes()).toContain('px-6');
    expect(body.classes()).toContain('px-6');
  });

  // A-tts defect 2: on a phone the panel sat at x=24 with square corners while the other tabs sat at x=16. The frame
  // runs panels edge to edge there, so the wrapper drops its inset and the strip keeps the 1rem phone inset.
  it('drops the panel inset on a phone and keeps the 1rem strip inset', async () => {
    const { wrapper } = await boardAt(phoneWidth);
    const strip = wrapper.get('[data-slot="board-grid"] > [data-slot="tabs"]');
    const body = wrapper.get('[data-slot="board-grid"] > div:not([data-slot])');

    expect(strip.classes()).toContain('max-md:px-4');
    expect(body.classes()).toContain('max-md:px-0');
  });

  // The trigger underline sits 5 px below the strip; inside a sideways scroller it made the strip scroll 4 px vertically.
  it('scrolls sideways only, never vertically', async () => {
    const { wrapper } = await boardAt(phoneWidth);
    const strip = wrapper.get('[data-slot="board-grid"] > [data-slot="tabs"]');

    expect(strip.classes()).toContain('overflow-x-auto');
    expect(strip.classes()).toContain('overflow-y-hidden');
  });
});
