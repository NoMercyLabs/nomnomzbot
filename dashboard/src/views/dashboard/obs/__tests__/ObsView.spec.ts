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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ObsView from '../ObsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: string[] = [];
let connection: Record<string, unknown> = {};
let widgets: Record<string, unknown>[] = [];
let liveFails: boolean = false;

const widget = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  isEnabled: true,
  isAttached: false,
  lastRuntimeError: null,
  eventSubscriptions: [],
  ...over,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push(path);

      if (path.endsWith('/widgets'))
        return { data: {
          data: widgets,
          hasMore: false,
          nextPage: null,
        }, response: { status: 200, ok: true } };

      if (path.endsWith('/obs/connection'))
        return { data: { data: connection }, response: { status: 200, ok: true } };

      if (path.endsWith('/obs/bridge/status'))
        return {
          data: { data: { instanceCount: 1, hasLeader: true } },
          response: { status: 200, ok: true },
        };

      if (liveFails)
        return { error: { status: 'error' }, response: { status: 502, ok: false } };

      if (path.endsWith('/obs/state'))
        return {
          data: { data: {
            currentScene: 'Main',
            streaming: true,
            recording: false,
          } },
          response: { status: 200, ok: true },
        };

      return {
        data: { data: [{ name: 'Main', isCurrent: true }, { name: 'BRB', isCurrent: false }] },
        response: { status: 200, ok: true },
      };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  liveFails = false;

  connection = {
    isEnabled: true,
    mode: 'websocket',
    host: 'localhost',
    port: 4455,
    lastError: null,
  };

  widgets = [
    widget('w1', 'Chat', { isAttached: true }),
    widget('w2', 'Alerts'),
    widget('w3', 'Feather', { lastRuntimeError: 'TypeError: feather is undefined' }),
    widget('w4', 'Audio', { isEnabled: false }),
  ];

  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(ObsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        [VueQueryPlugin, { queryClient }],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const dotsOf = (name: string): string[] =>
  Array.from(document.querySelectorAll(`[data-obs-dot="${name}"]`)).map((dot) => dot.className);

describe('ObsView', () => {
  it('shows each browser source as green loaded, orange not loaded, red failed or grey off, with its reason', async () => {
    await open();

    const rows = Array.from(document.querySelectorAll('[data-obs-source]'));

    expect(rows.map((row) => row.querySelector('.dot')?.className.replace('dot', '').trim())).toEqual([
      'ok',
      'warn',
      'bad',
      '',
    ]);

    expect(rows[0]?.textContent).toContain(en.dashboard.obs.sources.loaded);
    expect(rows[1]?.textContent).toContain(en.dashboard.obs.sources.notLoaded);
    expect(rows[2]?.textContent).toContain('TypeError: feather is undefined');
    expect(rows[3]?.textContent).toContain(en.dashboard.obs.sources.off);
  });

  it('marks the connection green, lists the scenes with the live one, and shows the stream state', async () => {
    await open();

    expect(dotsOf('connection')).toEqual(['dot ok']);
    expect(dotsOf('bridge')).toEqual(['dot ok']);

    const scenes = Array.from(document.querySelectorAll('[data-obs-scene]'));

    expect(scenes.map((scene) => scene.getAttribute('data-current'))).toEqual(['true', 'false']);
    expect(document.querySelector('[data-obs-live]')?.textContent).toContain(en.dashboard.obs.live.streaming);
  });

  it('turns the connection red with the bot reason and the header dot red when OBS reports an error', async () => {
    connection = { ...connection, lastError: 'Authentication failed' };
    await open();

    expect(dotsOf('connection')).toEqual(['dot bad']);
    expect(document.querySelector('[data-obs-connection]')?.textContent).toContain('Authentication failed');
    expect(document.querySelector('[data-obs-overall] .dot')?.className).toContain('bad');
  });

  it('turns the connection orange when OBS is linked but refuses the live reads', async () => {
    liveFails = true;
    await open();

    expect(dotsOf('connection')).toEqual(['dot warn']);
  });

  it('reads no scenes or state while the OBS link is switched off', async () => {
    connection = { ...connection, isEnabled: false };
    await open();

    expect(calls.some((path) => path.endsWith('/obs/scenes') || path.endsWith('/obs/state'))).toBe(false);
    expect(dotsOf('connection')).toEqual(['dot']);
    expect(document.body.textContent).toContain(en.dashboard.obs.scenes.offline);
  });
});
