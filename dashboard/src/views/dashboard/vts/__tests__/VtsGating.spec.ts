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
import VtsView from '../VtsView.vue';

const connection = {
  mode: 'direct',
  endpoint: 'ws://localhost:8001',
  hasPluginToken: true,
  hasBridgeToken: false,
  eventSubscriptionsMask: 255,
  isEnabled: true,
  status: 'connected',
};

const catalog = {
  models: [
    {
      id: 'm1',
      name: 'Hiyori',
      isLoaded: true,
    },
  ],
  hotkeys: [
    {
      id: 'h1',
      name: 'Wave',
      type: 'TriggerAnimation',
    },
  ],
  expressions: ['smile.exp3.json'],
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
      data: {
        data: path.endsWith('/vts/connection')
          ? connection
          : catalog,
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

async function open(view: string, keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  });

  await router.push(`/?view=${view}`);

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(VtsView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('VtsView shows only what the person may use', () => {
  it('shows no status, tabs or panel without any key', async () => {
    const view = await open('connection', []);

    expect(view.find('[data-vts-overall]').exists()).toBe(false);
    expect(view.find('[data-vts-connection]').exists()).toBe(false);
  });

  it('shows the connection locked and without save with vts:config:read alone', async () => {
    const view = await open('connection', ['vts:config:read']);

    expect(view.find('[data-vts-overall]').exists()).toBe(true);
    expect(view.find('[data-vts-connection]').exists()).toBe(true);
    expect(view.find('[data-vts-save]').exists()).toBe(false);

    const endpoint = view.find<HTMLInputElement>('[data-vts-endpoint]').element;

    expect(endpoint.closest('fieldset')?.disabled).toBe(true);
  });

  it('shows save and an open form with vts:config:write', async () => {
    const view = await open('connection', ['vts:config:read', 'vts:config:write']);

    expect(view.find('[data-vts-save]').exists()).toBe(true);

    const endpoint = view.find<HTMLInputElement>('[data-vts-endpoint]').element;

    expect(endpoint.closest('fieldset')?.disabled).toBe(false);
  });

  it('hides the hotkey and expression actions without vts:control', async () => {
    const hotkeys = await open('hotkeys', ['vts:config:read']);

    expect(hotkeys.find('[data-vts-trigger]').exists()).toBe(false);
    expect(hotkeys.find('[data-vts-load-model]').exists()).toBe(false);
    hotkeys.unmount();
    mounted = null;

    const expressions = await open('expressions', ['vts:config:read']);

    expect(expressions.find('[data-vts-activate]').exists()).toBe(false);
    expect(expressions.find('[data-vts-deactivate]').exists()).toBe(false);
  });

  it('shows the hotkey and expression actions with vts:control', async () => {
    const hotkeys = await open('hotkeys', ['vts:config:read', 'vts:control']);

    expect(hotkeys.find('[data-vts-trigger]').exists()).toBe(true);
    expect(hotkeys.find('[data-vts-load-model]').exists()).toBe(true);
    hotkeys.unmount();
    mounted = null;

    const expressions = await open('expressions', ['vts:config:read', 'vts:control']);

    expect(expressions.find('[data-vts-activate]').exists()).toBe(true);
    expect(expressions.find('[data-vts-deactivate]').exists()).toBe(true);
  });
});
