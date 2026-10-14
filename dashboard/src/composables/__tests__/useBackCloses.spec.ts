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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createRouter, createWebHistory, type Router } from 'vue-router';

import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog';
import { useBackCloses, useBackLayer } from '@/composables/useBackCloses';

let router: Router;
let page: VueWrapper | undefined;

function setWidth(width: number): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: !query.includes('width < 48rem') || width < 768,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

// Waits for the history move and the layers it opens or closes to settle.
async function settle(): Promise<void> {
  await flushPromises();

  await new Promise((resolve) => {
    setTimeout(resolve, 20);
  });

  await flushPromises();
}

async function goBack(): Promise<void> {
  window.history.back();
  await settle();
}

function layerCount(): number {
  return document.querySelectorAll('[data-dismissable-layer][role="dialog"]').length;
}

async function mountPage(): Promise<{ open: ReturnType<typeof ref<boolean>> }> {
  const open = ref(false);

  page = mount(defineComponent({
    setup() {
      useBackCloses();

      return () =>
        h(Dialog, {
          open: open.value,
          'onUpdate:open': (value: boolean) => {
            open.value = value;
          },
        }, () =>
          h(DialogContent, null, () =>
            h(DialogTitle, null, () =>
              'Menu')));
    },
  }), { attachTo: document.body, global: { plugins: [router] } });

  await router.isReady();

  return { open };
}

beforeEach(async () => {
  // Each test starts on a clean entry: a push to the same page keeps the last test's layer steps.
  window.history.replaceState(null, '', '/');

  router = createRouter({
    history: createWebHistory(),
    routes: [{ path: '/:any(.*)*', component: { render: () => null } }],
  });

  await router.push('/chat');
});

afterEach(() => {
  page?.unmount();
  page = undefined;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

describe('a menu on a phone', () => {
  it('closes on the phone back button and leaves the page where it was', async () => {
    setWidth(400);

    const { open } = await mountPage();
    const before = window.history.length;

    open.value = true;
    await settle();
    expect(layerCount()).toBe(1);
    expect(window.history.length).toBe(before + 1);

    await goBack();
    expect(open.value).toBe(false);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  // Stoney 2026-10-14: "Clicking send redirects after sending". Send closed the picker by a tap, and the back
  // step that took the picker's step away reloaded the chat.
  it('stays put when a tap closes it, and the next back leaves the page', async () => {
    setWidth(400);
    await router.push('/home');
    await router.push('/chat');

    const { open } = await mountPage();

    open.value = true;
    await settle();
    expect(window.history.state?.backLayers).toBe(1);

    const position = window.history.state?.position;

    open.value = false;
    await settle();
    expect(window.history.state?.position).toBe(position);
    expect(router.currentRoute.value.fullPath).toBe('/chat');

    await goBack();
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  // Stoney 2026-10-14: "Can't access any of the more pages". A page link in the More sheet closes the sheet
  // while it navigates; the page must open, and back must return to the page the sheet was on.
  it('lets a page link inside it open that page', async () => {
    setWidth(400);

    const { open } = await mountPage();

    open.value = true;
    await settle();

    open.value = false;
    await router.push('/overlays');
    await settle();
    expect(router.currentRoute.value.fullPath).toBe('/overlays');

    await goBack();
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });

  // Stoney 2026-10-14: "still not every triggerable menu allows the back button to close it without redirect".
  // A select or a message menu opens on the finger going down, before the tap counts as a gesture, and the
  // browser's back button skips a step added without one. The step waits for the finger to lift.
  it('adds its step once the tap counts as a gesture', async () => {
    setWidth(400);

    const activation = { isActive: false };

    Object.defineProperty(navigator, 'userActivation', { value: activation, configurable: true });

    const { open } = await mountPage();
    const position = window.history.state?.position;

    open.value = true;
    await settle();
    expect(window.history.state?.position).toBe(position);
    expect(window.history.state?.backLayers).toBeUndefined();

    activation.isActive = true;
    window.dispatchEvent(new Event('pointerup'));
    await settle();
    expect(window.history.state?.position).toBe(position + 1);
    expect(window.history.state?.backLayers).toBe(1);

    Reflect.deleteProperty(navigator, 'userActivation');
  });

  // A long press (the emote tone picker) ends in pointercancel, with no pointerup; the lift is a touchend.
  it('adds its step when a long press lifts', async () => {
    setWidth(400);

    const activation = { isActive: false };

    Object.defineProperty(navigator, 'userActivation', { value: activation, configurable: true });

    const { open } = await mountPage();
    const position = window.history.state?.position;

    open.value = true;
    await settle();
    window.dispatchEvent(new Event('pointercancel'));
    activation.isActive = true;
    window.dispatchEvent(new Event('touchend'));
    await settle();
    expect(window.history.state?.position).toBe(position + 1);
    expect(window.history.state?.backLayers).toBe(1);

    Reflect.deleteProperty(navigator, 'userActivation');
  });

  // The chat suggestion list, the reply bar and the code editor are not reka layers; they sign up instead.
  it('closes a panel that is not a reka layer, and only that panel', async () => {
    setWidth(400);

    const panel = ref(false);

    page = mount(defineComponent({
      setup() {
        useBackCloses();

        useBackLayer(panel, () => {
          panel.value = false;
        });

        return () =>
          panel.value
            ? h('div', { 'data-panel': '' })
            : null;
      },
    }), { attachTo: document.body, global: { plugins: [router] } });

    await router.isReady();

    panel.value = true;
    await settle();
    expect(window.history.state?.backLayers).toBe(1);

    await goBack();
    expect(panel.value).toBe(false);
    expect(router.currentRoute.value.fullPath).toBe('/chat');
  });
});

describe('a menu on a wide screen', () => {
  it('leaves the browser history alone', async () => {
    setWidth(1440);

    const { open } = await mountPage();
    const before = window.history.length;

    open.value = true;
    await settle();
    expect(layerCount()).toBe(1);
    expect(window.history.length).toBe(before);
  });
});
