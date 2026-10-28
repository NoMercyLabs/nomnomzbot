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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { defineComponent, h, type WritableComputedRef } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { usePageView } from '../usePageView';

type Opened = {
  router: Router;
  tab: WritableComputedRef<string>;
  panel: WritableComputedRef<string | undefined>;
};

async function open(query: string): Promise<Opened> {
  const blank = { template: '<div />' };
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/x', component: blank }],
  });

  await router.push(`/app/x${query}`);

  let tab!: WritableComputedRef<string>;
  let panel!: WritableComputedRef<string | undefined>;

  mount(
    defineComponent({
      setup: () => {
        tab = usePageView(['one', 'two'] as const);
        panel = usePageView(() => ['a', 'b'], 'panel');

        return () => h('div');
      },
    }),
    { global: { plugins: [router] } },
  );

  return {
    router,
    tab,
    panel,
  };
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

describe('usePageView', () => {
  // T6c defect 8: panel=rules stayed in the link after a tab switch, so the new tab carried a stale phone-panel id.
  it('drops the phone panel from the link when another tab opens', async () => {
    const { router, tab } = await open('?view=one&panel=b');

    tab.value = 'two';
    await settle();

    expect(router.currentRoute.value.query).toEqual({ view: 'two' });
  });

  it('keeps the open tab in the link when a phone panel opens', async () => {
    const { router, panel } = await open('?view=two');

    panel.value = 'b';
    await settle();

    expect(router.currentRoute.value.query).toEqual({ view: 'two', panel: 'b' });
  });
});
