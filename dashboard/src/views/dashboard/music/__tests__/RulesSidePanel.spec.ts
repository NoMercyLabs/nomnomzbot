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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';

import MusicView from '../MusicView.vue';
import en from '../i18n/en.json';

// Stoney 2026-10-19: "i asked you to make it a togglable menu to prevent this crap!" The request rules are an
// side panel, closed by default; it never squeezes the board's own panels. Stoney 2026-10-20 then asked for it as
// a third column where it fits (see the placement tests below).
const config = {
  isEnabled: true,
  preferredProvider: 'spotify',
  maxQueueSize: 20,
  maxRequestsPerUser: 1,
  allowYouTube: false,
  allowSpotify: true,
  minTrustLevel: 'everyone',
  bangerAutoCreate: false,
  maxSongSeconds: 270,
};

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      const data: unknown = path.endsWith('/music/config')
        ? config
        : null;

      return { data: { data }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  localStorage.clear();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

async function mountAt(width: number): Promise<void> {
  setWidth(width);

  const blank = { template: '<div />' };
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/app/music',
      name: 'dashboard-music',
      component: blank,
    }],
  });

  await router.push('/app/music');

  mounted = mount(MusicView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
}

function trigger(): HTMLButtonElement | undefined {
  return [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === 'Request rules',
  );
}

function sheet(): HTMLElement | null {
  return document.body.querySelector<HTMLElement>('[data-slot="side-panel"]');
}

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

describe.each([1280, 1920])('music request rules side panel at %i px', (width) => {
  it('is closed by default and never a column of the board', async () => {
    await mountAt(width);

    expect(sheet()).toBeNull();
    expect(document.body.querySelector('[data-panel="rules"]')).toBeNull();
    expect(trigger()).toBeDefined();
  });

  it('opens from the header button with the rules form and its Discard and Save', async () => {
    await mountAt(width);
    trigger()?.click();
    await settle();

    const open = sheet();

    expect(open?.textContent).toContain('Discard');
    expect(open?.textContent).toContain('Save');
  });

  it('closes with its close button', async () => {
    await mountAt(width);
    trigger()?.click();
    await settle();

    sheet()?.querySelector<HTMLButtonElement>('button[aria-label="Close"]')?.click();
    await settle();

    expect(sheet()).toBeNull();
  });

  it('closes with Escape', async () => {
    await mountAt(width);
    trigger()?.click();
    await settle();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(sheet()).toBeNull();
  });

  // The page's one other switch is playback, on the Now playing panel (Stoney 2026-10-19, "extra useless
  // dropdowns": it left a one-item menu); the requests switch lives only in the rules panel.
  it('leaves no rules switch in the header, since the button is the one control', async () => {
    await mountAt(width);

    const switches = [...document.body.querySelectorAll('[role="switch"]')];

    expect(switches.filter((toggle) => !toggle.closest('[data-music-playback]'))).toEqual([]);
  });
});

// Stoney 2026-10-20: "the request rules pannel proably fits just fine as a 3rd column on this breakpont and must
// be consistent with the other pannel behaviors." Wide, it is a column beside the board; narrow, a sheet.
describe('music request rules side panel placement', () => {
  it('opens as a third column beside the board where the window is wide', async () => {
    await mountAt(1440);
    trigger()?.click();
    await settle();

    const column = document.body.querySelector<HTMLElement>('aside[data-slot="side-panel"]');

    expect(column).not.toBeNull();
    expect(document.body.querySelector('[role="dialog"][data-slot="side-panel"]')).toBeNull();
    expect(column?.previousElementSibling?.querySelector('[data-panel="now"]')).not.toBeNull();
  });

  it('opens as a sheet over the page where the window is narrow', async () => {
    await mountAt(900);
    trigger()?.click();
    await settle();

    expect(document.body.querySelector('aside[data-slot="side-panel"]')).toBeNull();
    expect(document.body.querySelector('[role="dialog"][data-slot="side-panel"]')).not.toBeNull();
  });
});
