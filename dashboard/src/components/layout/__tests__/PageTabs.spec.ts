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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';

const tabs: PageTab[] = [
  { value: 'info', label: 'Stream info' },
  { value: 'run', label: 'Run the stream' },
  {
    value: 'history',
    label: 'History',
    count: 12,
  },
];

async function open(path: string): Promise<{ router: Router; wrapper: VueWrapper }> {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { render: () => null } }],
  });

  await router.push(path);
  const wrapper: VueWrapper = mount(PageTabs, { props: { tabs }, global: { plugins: [router] } });

  await flushPromises();

  return { router, wrapper };
}

// jsdom has no layout: every tab is 100 wide, the strip 200 wide, and a tab sits 300 further along per place.
function stubLayout(): void {
  const places = new Map<string, number>([['Run the stream', 1], ['History 12', 2]]);

  Object.defineProperty(HTMLElement.prototype, 'offsetLeft', {
    configurable: true,
    get(this: HTMLElement): number {
      return (places.get(this.textContent?.trim() ?? '') ?? 0) * 300;
    },
  });

  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get: () => 100 });
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 200 });
}

function selectedLabel(wrapper: VueWrapper): string | undefined {
  return wrapper
    .findAll('[role="tab"]')
    .find((tab) => tab.attributes('aria-selected') === 'true')
    ?.text();
}

describe('the page tabs', () => {
  it('opens the tab named in the query, so a link lands on it', async () => {
    const { wrapper } = await open('/?view=run');

    expect(selectedLabel(wrapper)).toBe('Run the stream');
  });

  it('falls back to the first tab when the query names an unknown tab', async () => {
    const { wrapper } = await open('/?view=gone');

    expect(selectedLabel(wrapper)).toBe('Stream info');
  });

  it('puts the chosen tab in the query and keeps other query values', async () => {
    const { router, wrapper } = await open('/?q=abc');

    await wrapper.findAll('[role="tab"]')[1]!.trigger('mousedown', { button: 0 });
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({ q: 'abc', view: 'run' });
    expect(selectedLabel(wrapper)).toBe('Run the stream');
  });

  it('returns to the earlier tab on Back', async () => {
    const { router, wrapper } = await open('/');

    await wrapper.findAll('[role="tab"]')[2]!.trigger('mousedown', { button: 0 });
    await flushPromises();
    expect(router.currentRoute.value.query.view).toBe('history');

    router.back();
    await flushPromises();

    expect(selectedLabel(wrapper)).toBe('Stream info');
  });

  it('shows the count of a tab beside its label', async () => {
    const { wrapper } = await open('/');

    expect(wrapper.findAll('[role="tab"]')[2]!.text()).toBe('History 12');
  });

  // A phone cuts a long strip at the edge: the edge that still hides tabs fades, so Keys is not a hard cut.
  it('fades the edge that hides tabs and nothing when all tabs fit', async () => {
    const { wrapper } = await open('/');
    const strip = wrapper.get('[data-slot="tabs"]');

    expect(strip.classes().some((name) => name.startsWith('mask-'))).toBe(false);

    const element = strip.element as HTMLElement;

    Object.defineProperty(element, 'scrollWidth', { configurable: true, value: 500 });
    Object.defineProperty(element, 'clientWidth', { configurable: true, value: 200 });
    element.scrollLeft = 0;
    await strip.trigger('scroll');
    await flushPromises();
    expect(strip.classes()).toContain('mask-r-from-90%');

    element.scrollLeft = 300;
    await strip.trigger('scroll');
    await flushPromises();
    expect(strip.classes()).toContain('mask-l-from-90%');
    expect(strip.classes()).not.toContain('mask-r-from-90%');
  });

  // At 390 px the last tab sat past the edge; opening it by link left the active tab out of sight.
  it('moves the strip to the active tab on open and when the tab changes', async () => {
    stubLayout();

    const { router, wrapper } = await open('/?view=history');
    const strip = wrapper.get('[data-slot="tabs"]').element;

    // History sits at 600 in a 200 wide strip, 100 wide itself: centered, the strip starts at 550.
    expect(strip.scrollLeft).toBe(550);

    await router.push('/?view=run');
    await flushPromises();

    expect(strip.scrollLeft).toBe(250);
  });

  // The trigger underline sits 5 px below the strip; inside a sideways scroller it made the strip scroll 4 px vertically.
  it('scrolls sideways only, never vertically', async () => {
    const { wrapper } = await open('/');
    const strip = wrapper.get('[data-slot="tabs"]');

    expect(strip.classes()).toContain('overflow-x-auto');
    expect(strip.classes()).toContain('overflow-y-hidden');
  });

  // The home page mounts previews before they are seen; scrollIntoView on their tab strip scrolled the whole page.
  it('never scrolls the page, only its own strip', async () => {
    stubLayout();

    let pageScrolls = 0;

    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value(): void {
        pageScrolls += 1;
      },
    });

    await open('/?view=history');

    expect(pageScrolls).toBe(0);
  });
});
