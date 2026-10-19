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

import type { ObsConnection, ObsState } from '@/api/obs';
import { i18n } from '@/i18n';

import ObsConnectionPanel from '../components/ObsConnectionPanel.vue';
import OutputsPanel from '../components/OutputsPanel.vue';
import en from '../i18n/en.json';

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
    GET: async () => ({
      data: { data: {} },
      response: { status: 200, ok: true },
    }),
  },
}));

const state = {
  currentScene: 'Main',
  streaming: false,
  recording: false,
} as unknown as ObsState;

const connection = {
  isEnabled: true,
  mode: 'direct',
  host: 'localhost',
  port: 4455,
  lastError: null,
} as unknown as ObsConnection;

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

function plugins(): unknown[] {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return [i18n, [VueQueryPlugin, { queryClient }]];
}

async function openOutputs(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  mounted = mount(OutputsPanel, {
    attachTo: document.body,
    props: {
      channelId: 'c1',
      state,
      virtualCam: false,
      streamTimer: '',
      recordTimer: '',
      panelState: 'ready',
      offline: false,
    },
    global: { plugins: plugins() as never },
  });

  await flushPromises();

  return mounted;
}

async function openConnection(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  mounted = mount(ObsConnectionPanel, {
    attachTo: document.body,
    props: {
      connection,
      bridge: null,
      state,
      unreachable: false,
      panelState: 'ready',
    },
    global: { plugins: plugins() as never },
  });

  await flushPromises();

  return mounted;
}

describe('OBS shows only what the person may use', () => {
  it('disables the start buttons with the reason without obs:control:broadcast', async () => {
    const view = await openOutputs(['obs:control']);
    const start = view.find('[data-output="streaming"] [data-act="start"]');
    const scope = start.element.closest('[data-slot="read-only-scope"]');

    expect(view.find('[data-output="streaming"]').exists()).toBe(true);
    expect(scope?.hasAttribute('disabled')).toBe(true);
    expect(scope?.getAttribute('title')).toBe(en.dashboard.obs.needsBroadcast);
    expect(view.find('[data-output="recording"] [data-act="start"]').exists()).toBe(true);
  });

  it('enables the start buttons with obs:control:broadcast', async () => {
    const view = await openOutputs(['obs:control', 'obs:control:broadcast']);
    const start = view.find('[data-output="streaming"] [data-act="start"]');
    const scope = start.element.closest('[data-slot="read-only-scope"]');

    expect(scope?.hasAttribute('disabled')).toBe(false);
    expect(view.find('[data-output="recording"] [data-act="start"]').exists()).toBe(true);
  });

  it('shows the connection form only with obs:config:write', async () => {
    const reader = await openConnection(['obs:config:read']);

    expect(reader.find('[data-obs-host]').exists()).toBe(false);

    reader.unmount();
    mounted = null;

    const writer = await openConnection(['obs:config:read', 'obs:config:write']);

    expect(writer.find('[data-obs-host]').exists()).toBe(true);
  });
});
