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

// Stoney 2026-10-18: "why is "Request rules" in a drawer instead of inline? there is enough space for it on
// my current size." Where the board has its columns the rules sit in it; only a narrow board uses the drawer.
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

describe('music request rules placement', () => {
  it('puts the rules in the board and shows no drawer button when the board is wide', async () => {
    await mountAt(1800);

    const cell = document.body.querySelector<HTMLElement>('[data-panel="rules"]');

    expect(cell?.querySelector('[data-slot="panel-card"]')?.textContent).toContain('Request rules');
    expect(cell?.querySelector('input[name="song-seconds"]')).not.toBeNull();
    expect(trigger()).toBeUndefined();
    expect(document.body.querySelector('[data-slot="sheet-content"]')).toBeNull();
  });

  it('keeps the rules behind a drawer button when the board is narrow', async () => {
    await mountAt(900);

    expect(document.body.querySelector('[data-panel="rules"]')).toBeNull();

    trigger()?.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 0));
    await flushPromises();

    const sheet = document.body.querySelector<HTMLElement>('[data-slot="sheet-content"]');

    expect(sheet?.querySelector('input[name="song-seconds"]')).not.toBeNull();
  });
});
