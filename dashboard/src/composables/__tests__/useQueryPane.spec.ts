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

// Stoney 2026-11-03: "a close is a history pop". These tests run the real router and the real history.
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { defineComponent, type WritableComputedRef } from 'vue';
import { createRouter, createWebHistory, type Router } from 'vue-router';

import { usePageView } from '@/composables/usePageView';
import { useQueryPane, type QueryPane } from '@/composables/useQueryPane';

let router: Router;
let wrapper: VueWrapper | undefined;
let pane: QueryPane;

async function settle(): Promise<void> {
  await flushPromises();

  await new Promise((resolve) => {
    setTimeout(resolve, 40);
  });

  await flushPromises();
}

function position(): number {
  return window.history.state?.position;
}

async function mountPane(companions: readonly string[] = []): Promise<void> {
  wrapper = mount(
    defineComponent({
      setup() {
        pane = useQueryPane('user', companions);

        return () => null;
      },
    }),
    { global: { plugins: [router] } },
  );

  await router.isReady();
}

async function goBack(): Promise<void> {
  window.history.back();
  await settle();
}

beforeEach(async () => {
  window.history.replaceState(null, '', '/');

  router = createRouter({
    history: createWebHistory(),
    routes: [{ path: '/:any(.*)*', component: { render: () => null } }],
  });

  await router.push('/home');
  await router.push('/chat');
});

afterEach(() => {
  wrapper?.unmount();
  wrapper = undefined;
});

describe('useQueryPane', () => {
  it('opens with one step and closes by popping it, then Back leaves the page', async () => {
    await mountPane();
    const start = position();

    pane.open('a');
    await settle();
    expect(position()).toBe(start + 1);
    expect(pane.id.value).toBe('a');

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
    expect(pane.id.value).toBeNull();

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('replaces when another id opens, so a close still leaves one step to pop', async () => {
    await mountPane();
    const start = position();

    pane.open('a');
    await settle();
    pane.open('b');
    await settle();
    expect(position()).toBe(start + 1);
    expect(router.currentRoute.value.fullPath).toBe('/chat?user=b');

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('closes a pasted link by replacing, so Back leaves the page', async () => {
    await mountPane();
    await router.replace('/chat?user=a');
    await settle();
    const start = position();

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('keeps the other query keys through open and close', async () => {
    await mountPane();
    await router.push('/chat?tab=stats&sort=name');
    await settle();
    const start = position();

    pane.open('a');
    await settle();

    expect(router.currentRoute.value.query).toEqual({
      tab: 'stats',
      sort: 'name',
      user: 'a',
    });

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.query).toEqual({ tab: 'stats', sort: 'name' });
  });

  it('drops the companion keys on open and on close', async () => {
    await mountPane(['tab']);
    await router.push('/chat?tab=stats');
    await settle();

    pane.open('a');
    await settle();
    expect(router.currentRoute.value.query).toEqual({ user: 'a' });

    await router.replace('/chat?user=a&tab=notes');
    await settle();
    pane.close();
    await settle();
    expect(router.currentRoute.value.query).toEqual({});
  });
});

// The tab strip and the pane share one history: a tab pushed before the pane is what a close pops back to,
// and a tab changed inside the open pane replaces its entry instead of stacking another.
describe('useQueryPane with a tab strip', () => {
  let tab: WritableComputedRef<'stats' | 'notes'>;

  function mountBoth(companions: readonly string[], tabKey: string): void {
    wrapper = mount(
      defineComponent({
        setup() {
          pane = useQueryPane('user', companions);
          tab = usePageView(['stats', 'notes'], tabKey);

          return () => null;
        },
      }),
      { global: { plugins: [router] } },
    );
  }

  it('pops back to the tab entry that was there before the pane opened', async () => {
    mountBoth([], 'view');
    await router.isReady();
    tab.value = 'notes';
    await settle();
    const start = position();

    pane.open('a');
    await settle();
    expect(position()).toBe(start + 1);

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat?view=notes');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  it('keeps a filter changed inside the pane by popping the pane and taking the filter in place', async () => {
    mountBoth([], 'view');
    await router.isReady();
    const start = position();

    pane.open('a');
    await settle();
    await router.replace({ query: { user: 'a', sort: 'name' } });
    await settle();

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat?sort=name');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('replaces the entry when a companion tab changes inside the open pane, so a close leaves the start position', async () => {
    mountBoth(['tab'], 'tab');
    await router.isReady();
    const start = position();

    pane.open('a');
    await settle();
    tab.value = 'notes';
    await settle();
    expect(position()).toBe(start + 1);
    expect(router.currentRoute.value.fullPath).toBe('/chat?user=a&tab=notes');

    pane.close();
    await settle();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });
});
