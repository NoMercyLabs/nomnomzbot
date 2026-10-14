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

const blockedRow = {
  id: 'b1',
  provider: 'spotify',
  trackUri: 'spotify:track:b1',
  title: 'Baby Shark',
  reason: null,
  blockedByUserId: null,
  createdAt: '2026-10-01T10:00:00Z',
};
const config = {
  isEnabled: true,
  preferredProvider: 'spotify',
  maxQueueSize: 20,
  maxRequestsPerUser: 1,
  allowYouTube: false,
  allowSpotify: true,
  minTrustLevel: 'everyone',
  bangerAutoCreate: false,
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      let data: unknown = null;

      if (path.endsWith('/music/config')) {
        data = config;
      } else if (path.endsWith('/music/blocked-tracks')) {
        data = [blockedRow];
      }

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
});

async function openDrawer(): Promise<HTMLElement> {
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

  const trigger = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === 'Request rules',
  );

  trigger?.click();
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();

  return document.body.querySelector<HTMLElement>('[data-slot="sheet-content"]') as HTMLElement;
}

describe('music request rules drawer on a phone', () => {
  // The drawer is fixed to the viewport. On a phone the browser bars change the visible height,
  // so the drawer is sized to the dynamic viewport and its footer stays on screen.
  it('sizes the drawer to the dynamic viewport so the footer stays on screen', async () => {
    const sheet = await openDrawer();

    expect(sheet.className).toContain('data-[side=right]:h-dvh');
    expect(sheet.className).toContain('data-[side=right]:max-h-dvh');
  });

  it('keeps the body scrolling above a footer that holds Discard and Save', async () => {
    const sheet = await openDrawer();
    const panel = sheet.querySelector<HTMLElement>('[data-slot="panel-card"]') as HTMLElement;
    const body = panel.querySelector<HTMLElement>(':scope > div') as HTMLElement;
    const footer = panel.querySelector<HTMLElement>(':scope > footer') as HTMLElement;

    expect(body.className).toContain('overflow-y-auto');
    expect(footer.className).toContain('flex-none');
    expect(footer.textContent).toContain('Discard');
    expect(footer.textContent).toContain('Save');
  });

  it('draws Allow as a bordered control, not loose text', async () => {
    const sheet = await openDrawer();
    const allow = sheet.querySelector<HTMLButtonElement>('button[aria-label="Allow Baby Shark"]');

    expect(allow?.textContent?.trim()).toBe('Allow');
    expect(allow?.className).toContain('border-border');
  });
});
