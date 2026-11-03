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

// Stoney 2026-11-03: "i want EVERYTHING to have a history state, and a close is a history pop". These tests run
// the real router, the real history and the real dialog, dropdown and select components.
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createRouter, createWebHistory, type Router } from 'vue-router';

import { Dialog, DialogClose, DialogContent, DialogTitle } from '@/components/ui/dialog';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem } from '@/components/ui/dropdown-menu';
import { Select, SelectContent, SelectItem } from '@/components/ui/select';
import { useBackCloses } from '@/composables/useBackCloses';

let router: Router;
let wrapper: VueWrapper | undefined;

const widths = [1200, 400] as const;

function setWidth(width: number): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: !query.includes('max-width') || width < 768,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

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

function steps(): number {
  return window.history.state?.backLayers ?? 0;
}

function layers(): number {
  return document.querySelectorAll('[data-dismissable-layer]:not([data-state="closed"])').length;
}

async function mountPage() {
  const dialog = ref(false);
  const nested = ref(false);
  const menu = ref(false);
  const select = ref(false);

  wrapper = mount(
    defineComponent({
      setup() {
        useBackCloses();

        return () => [
          h(Dialog, { open: dialog.value, 'onUpdate:open': (value: boolean) => { dialog.value = value; } }, () =>
            h(DialogContent, null, () => [
              h(DialogTitle, null, () => 'First'),
              h(DialogClose, { 'data-test-close': '' }, () => 'Close'),
            ])),
          h(Dialog, { open: nested.value, 'onUpdate:open': (value: boolean) => { nested.value = value; } }, () =>
            h(DialogContent, null, () =>
              h(DialogTitle, null, () => 'Second'))),
          h(DropdownMenu, { open: menu.value, 'onUpdate:open': (value: boolean) => { menu.value = value; } }, () =>
            h(DropdownMenuContent, null, () =>
              h(DropdownMenuItem, { 'data-test-item': '' }, () => 'Pick'))),
          h(Select, { open: select.value, 'onUpdate:open': (value: boolean) => { select.value = value; } }, () =>
            h(SelectContent, null, () =>
              h(SelectItem, { value: 'a', 'data-test-option': '' }, () => 'A'))),
        ];
      },
    }),
    { attachTo: document.body, global: { plugins: [router] } },
  );

  await router.isReady();

  return {
    dialog,
    nested,
    menu,
    select,
  };
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
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe.each(widths)('a close that is not Back, at %i px', (width) => {
  beforeEach(() => {
    setWidth(width);
  });

  async function expectPopped(start: number): Promise<void> {
    expect(layers()).toBe(0);
    expect(position()).toBe(start);
    expect(steps()).toBe(0);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    window.history.back();
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  }

  it('pops the step when the dialog closes with its own X', async () => {
    const { dialog } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(1);

    document.querySelector<HTMLElement>('[data-test-close]')?.click();
    await settle();
    expect(dialog.value).toBe(false);
    await expectPopped(start);
  });

  it('pops the step when Escape closes the dialog', async () => {
    const { dialog } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();
    expect(steps()).toBe(1);

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(dialog.value).toBe(false);
    await expectPopped(start);
  });

  it('pops the step when an outside click closes the dialog', async () => {
    const { dialog } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();
    expect(steps()).toBe(1);

    document.body.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));
    await settle();
    expect(dialog.value).toBe(false);
    await expectPopped(start);
  });

  it('takes a step for a dropdown menu and pops it on pick', async () => {
    const { menu } = await mountPage();
    const start = position();

    menu.value = true;
    await settle();
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(1);

    document.querySelector<HTMLElement>('[data-test-item]')?.click();
    await settle();
    expect(menu.value).toBe(false);
    await expectPopped(start);
  });

  it('takes a step for a select and pops it on pick', async () => {
    const { select } = await mountPage();
    const start = position();

    select.value = true;
    await settle();
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(1);

    document
      .querySelector('[data-test-option]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

    await settle();
    expect(select.value).toBe(false);
    await expectPopped(start);
  });

  it('pops two steps when two nested layers close in one tick', async () => {
    const { dialog, menu } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();
    menu.value = true;
    await settle();
    expect(steps()).toBe(2);

    dialog.value = false;
    menu.value = false;
    await settle();
    await expectPopped(start);
  });

  it('closes only the top layer on Back when two are open', async () => {
    const { dialog, menu } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();
    menu.value = true;
    await settle();
    expect(steps()).toBe(2);

    window.history.back();
    await settle();
    expect(layers()).toBe(1);
    expect(dialog.value).toBe(true);
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(1);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  // Stoney 2026-10-14: "Clicking send redirects after sending". The tap closes the layer and navigates in one tick.
  it('shows the new route, with no entry under it, when a close and a navigation share a tick', async () => {
    const { dialog } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();

    dialog.value = false;
    await router.push('/sent');
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/sent');
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(0);

    window.history.back();
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/chat');
    expect(position()).toBe(start);
  });

  it('waits for the pop, then lands once, when a navigation starts after the close', async () => {
    const { dialog } = await mountPage();
    const start = position();
    const length = window.history.length;
    const realGo = window.history.go.bind(window.history);

    dialog.value = true;
    await settle();
    expect(window.history.length).toBe(length + 1);

    // The pop stays in flight for 30 ms, so the navigation below starts while it is still pending.
    vi.spyOn(window.history, 'go').mockImplementation((delta) => {
      setTimeout(() => realGo(delta), 30);
    });

    dialog.value = false;
    await flushPromises();

    await new Promise((resolve) => {
      setTimeout(resolve, 0);
    });

    await router.push('/after');
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/after');
    expect(window.history.length).toBe(length + 1);
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(0);

    window.history.back();
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  it('leaves no dead entry when a same-path query push follows a close', async () => {
    const { dialog } = await mountPage();
    const start = position();

    dialog.value = true;
    await settle();

    dialog.value = false;
    await router.push('/chat?tab=two');
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/chat?tab=two');
    expect(position()).toBe(start + 1);
    expect(steps()).toBe(0);

    window.history.back();
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/chat');
    expect(position()).toBe(start);
  });

  it('adds a new step for a layer that stays open while the page navigates', async () => {
    const { dialog } = await mountPage();

    dialog.value = true;
    await settle();

    await router.push('/elsewhere');
    await settle();
    expect(layers()).toBe(1);
    expect(router.currentRoute.value.fullPath).toBe('/elsewhere');
    expect(steps()).toBe(1);

    window.history.back();
    await settle();
    expect(dialog.value).toBe(false);
    expect(router.currentRoute.value.fullPath).toBe('/elsewhere');
  });
});
