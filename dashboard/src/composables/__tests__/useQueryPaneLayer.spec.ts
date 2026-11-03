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

// Stoney 2026-11-03: "a close is a history pop". A pane that opens in a drawer (a Sheet is a layer with a step of
// its own) still takes ONE step: the pane and the layer share it. Real router, real history, real Sheet.
import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';
import type { Router } from 'vue-router';

import {
  BackHost,
  position,
  pressBack,
  settle,
  startHistory,
  steps,
} from '@/__tests__/backHistory';
import { Sheet, SheetContent, SheetDescription, SheetTitle } from '@/components/ui/sheet';
import { useQueryPane, type QueryPane } from '@/composables/useQueryPane';

let router: Router;
let wrapper: VueWrapper | undefined;
let pane: QueryPane;

const Drawer = defineComponent({
  setup() {
    pane = useQueryPane('user', [], { inLayer: true });

    return () =>
      h(
        Sheet,
        {
          open: pane.id.value !== null,
          'onUpdate:open': (open: boolean) => {
            if (!open) {
              pane.close();
            }
          },
        },
        () =>
          h(SheetContent, { 'data-drawer': '' }, () => [
            h(SheetTitle, () => 'Detail'),
            h(SheetDescription, () => 'Detail'),
          ]),
      );
  },
});

beforeEach(async () => {
  router = await startHistory();

  wrapper = mount(
    defineComponent({ render: () => h(BackHost, () =>
      h(Drawer)) }),
    {
      attachTo: document.body,
      global: { plugins: [router] },
    },
  );

  await router.isReady();
  await settle();
});

afterEach(() => {
  wrapper?.unmount();
  wrapper = undefined;
  document.body.innerHTML = '';
});

describe('a query pane that shows in a drawer', () => {
  it('opens with one history step, not two', async () => {
    const start = position();

    pane.open('a');
    await settle();

    expect(document.body.querySelector('[data-drawer]')).not.toBeNull();
    expect(router.currentRoute.value.fullPath).toBe('/chat?user=a');
    expect(position()).toBe(start + 1);
  });

  it('closes by its own button and leaves the start position, then Back leaves the page', async () => {
    const start = position();

    pane.open('a');
    await settle();
    pane.close();
    await settle();

    expect(document.body.querySelector('[data-drawer]')).toBeNull();
    expect(position()).toBe(start);
    expect(steps()).toBe(0);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await pressBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('closes a pasted link in the drawer without bringing it back, then Back leaves the page', async () => {
    await router.replace('/chat?user=a');
    await settle();
    expect(document.body.querySelector('[data-drawer]')).not.toBeNull();

    pane.close();
    await settle();
    expect(document.body.querySelector('[data-drawer]')).toBeNull();
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await pressBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('closes by one Back and leaves the start position', async () => {
    const start = position();

    pane.open('a');
    await settle();
    await pressBack();

    expect(document.body.querySelector('[data-drawer]')).toBeNull();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });
});
