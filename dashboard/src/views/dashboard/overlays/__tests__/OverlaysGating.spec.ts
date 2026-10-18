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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import OverlaysView from '../OverlaysView.vue';

const widget = {
  id: 'w1',
  name: 'Feather Alert',
  description: 'Feather Alert description',
  framework: 'vue',
  source: 'user',
  isEnabled: true,
  overlayUrl: 'https://bot.example/overlay?widgetId=w1',
  activeVersionId: null,
  galleryItemId: null,
  settings: {},
  eventSubscriptions: ['follow'],
  lastRuntimeError: null,
  lastRanAt: null,
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  galleryUpdateAvailable: false,
  isAttached: false,
  isCustomized: false,
};

const held = vi.hoisted(() => ({ keys: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => ({
      data: path.endsWith('/overlays/required')
        ? { data: [] }
        : {
            data: [widget],
            hasMore: false,
            nextPage: null,
          },
      response: { status: 200, ok: true },
    }),
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/overlays',
        name: 'dashboard-overlays',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push('/app/overlays?id=w1');

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(OverlaysView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('OverlaysView shows only what the person may use', () => {
  it('shows no list or detail without widget:read', async () => {
    const view = await open([]);

    expect(view.find('[data-overlay-row]').exists()).toBe(false);
    expect(view.find('[data-overlay-detail]').exists()).toBe(false);
  });

  it('shows the list and the on state, not the switch, with widget:read alone', async () => {
    const view = await open(['widget:read']);

    expect(view.find('[data-overlay-row]').exists()).toBe(true);
    expect(view.find('[data-overlay-detail] [role="switch"]').exists()).toBe(false);
    expect(view.find('[data-overlay-state]').text()).toBe('On');
  });

  it('shows the on switch instead of the state with widget:write', async () => {
    const view = await open(['widget:read', 'widget:write']);

    expect(view.find('[data-overlay-detail] [role="switch"]').exists()).toBe(true);
    expect(view.find('[data-overlay-state]').exists()).toBe(false);
  });
});
