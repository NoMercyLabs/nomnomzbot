// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import MusicView from '../MusicView.vue';
import en from '../i18n/en.json';

// Stoney 2026-10-19: "i asked you to make it a togglable menu to prevent this crap!" The request rules are an
// overlay side panel, closed by default, at every width; they are never a column squeezing the other panels.
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
  vi.stubGlobal('innerWidth', width);

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

  it('leaves no header switch for the rules, since the button is the one control', async () => {
    await mountAt(width);

    expect(document.body.querySelector('[role="switch"]')).toBeNull();
  });
});
