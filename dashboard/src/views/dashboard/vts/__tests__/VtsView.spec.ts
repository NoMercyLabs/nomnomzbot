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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import VtsView from '../VtsView.vue';

type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
let connection: Record<string, unknown> = {};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push({
        method: 'GET',
        path,
        body: undefined,
      });

      if (path.endsWith('/vts/connection'))
        return { data: { data: connection }, response: { status: 200, ok: true } };

      return {
        data: {
          data: {
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
          },
        },
        response: { status: 200, ok: true },
      };
    },
    PUT: async (path: string, init: { body?: unknown }) => {
      calls.push({
        method: 'PUT',
        path,
        body: init.body,
      });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, init: { body?: unknown }) => {
      calls.push({
        method: 'POST',
        path,
        body: init.body,
      });

      return { data: { data: { ok: true } }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

async function show(view: string): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }],
  });

  await router.push(`/?view=${view}`);

  const queries = new QueryClient();

  mounted = mount(VtsView, {
    attachTo: document.body,
    global: { plugins: [[VueQueryPlugin, { queryClient: queries }], i18n, router, createPinia()] },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  setActivePinia(createPinia());
  calls.length = 0;

  connection = {
    mode: 'direct',
    endpoint: 'ws://localhost:8001',
    hasPluginToken: true,
    hasBridgeToken: false,
    eventSubscriptionsMask: 255,
    eventKinds: [
      {
        name: 'General',
        bit: 1,
        onByDefault: true,
      },
      {
        name: 'Models',
        bit: 2,
        onByDefault: true,
      },
      {
        name: 'Hotkeys',
        bit: 4,
        onByDefault: false,
      },
    ],
    isEnabled: true,
    status: 'connected',
  };

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('VtsView', () => {
  it('saves the typed endpoint, mode and mask as one connection body', async () => {
    const view = await show('connection');

    await view.get('[data-vts-endpoint]').setValue('ws://other:8001');
    await view.get('[data-kind="Hotkeys"]').trigger('click');
    await view.get('[data-vts-save]').trigger('click');
    await flushPromises();

    const put = calls.find((call) => call.method === 'PUT');

    expect(put?.path).toBe('/api/v{version}/channels/{channelId}/vts/connection');

    expect(put?.body).toEqual({
      mode: 'direct',
      endpoint: 'ws://other:8001',
      eventSubscriptionsMask: 251,
      isEnabled: true,
    });
  });

  it('shows the waiting state and starts the one-time approval', async () => {
    connection = {
      ...connection,
      hasPluginToken: false,
      status: 'unauthorized',
    };

    const view = await show('connection');

    expect(view.get('[data-vts-overall]').text()).toContain(en.dashboard.vts.status.unauthorized);
    expect(view.get('[data-vts-token-dot]').classes()).toContain('warn');

    await view.get('[data-vts-authorize]').trigger('click');
    await flushPromises();

    expect(calls.some((call) => call.path.endsWith('/vts/connection/authorize'))).toBe(true);
  });

  it('does not read the inventory until VTube Studio is connected', async () => {
    connection = { ...connection, status: 'unauthorized' };
    await show('hotkeys');

    expect(calls.some((call) => call.path.endsWith('/vts/inventory'))).toBe(false);
  });

  it('loads the inventory once connected and keeps Trigger off until a hotkey is picked', async () => {
    const view = await show('hotkeys');

    expect(calls.some((call) => call.path.endsWith('/vts/inventory'))).toBe(true);
    expect(view.get('[data-vts-trigger]').attributes('disabled')).toBeDefined();
  });

  it('sends the expression file and active flag through the control route', async () => {
    const view = await show('expressions');
    const panel = view.findComponent({ name: 'VtsExpressionsPanel' });

    (panel.vm as unknown as { expression: string }).expression = 'smile.exp3.json';
    await flushPromises();
    await view.get('[data-vts-activate]').trigger('click');
    await flushPromises();

    const post = calls.find((call) => call.path.endsWith('/vts/control'));

    expect(post?.body).toEqual({
      requestType: 'ExpressionActivationRequest',
      payloadJson: JSON.stringify({ expressionFile: 'smile.exp3.json', active: true }),
    });
  });
});
