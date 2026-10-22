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
import { obsStateChanged } from '@/realtime/obs';

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
let required: Record<string, unknown>[] = [];
let liveFails: boolean = false;
let bridgeCount: number = 1;

const widget = (id: string, name: string, over: Record<string, unknown> = {}) =>
  ({
    id,
    name,
    isEnabled: true,
    isAttached: false,
    lastRuntimeError: null,
    eventSubscriptions: [],
    ...over,
  }) as const satisfies Record<string, unknown>;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (
      path: string,
      options?: { params?: { query?: { view?: string } } },
    ): Promise<Answer> => {
      const view = options?.params?.query?.view;

      calls.push(view
        ? `${path}?view=${view}`
        : path);

      // The server's rule: the full connection and the bridge need the configuration key.
      const refusedWithoutConfig =
        path.endsWith('/obs/bridge/status') ||
        (path.endsWith('/obs/connection') && view !== 'Status');

      if (refusedWithoutConfig && !held.includes('obs:config:read'))
        return { error: { status: 'error' }, response: { status: 403, ok: false } };

      if (path.endsWith('/overlays/required'))
        return { data: { data: required }, response: { status: 200, ok: true } };

      if (path.endsWith('/widgets'))
        return {
          data: {
            data: widgets,
            hasMore: false,
            nextPage: null,
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/obs/connection') && view === 'Status')
        return {
          data: { data: { mode: connection.mode, isEnabled: connection.isEnabled } },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/obs/connection'))
        return { data: { data: connection }, response: { status: 200, ok: true } };

      if (path.endsWith('/obs/bridge/status'))
        return {
          data: { data: { instanceCount: bridgeCount, hasLeader: bridgeCount > 0 } },
          response: { status: 200, ok: true },
        };

      if (liveFails)
        return { error: { status: 'error' }, response: { status: 502, ok: false } };

      if (path.endsWith('/obs/state'))
        return {
          data: {
            data: {
              currentScene: 'Main',
              streaming: true,
              recording: false,
            },
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/obs/hotkeys'))
        return {
          data: { data: ['OBSBasic.StartStreaming', 'OBSBasic.StopStreaming'] },
          response: { status: 200, ok: true },
        };

      return {
        data: {
          data: [
            { name: 'Main', isCurrent: true },
            { name: 'BRB', isCurrent: false },
          ],
        },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let held: string[] = [];

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (key: string): boolean => held.includes(key) }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  held = ['obs:scene:read', 'obs:control', 'obs:config:read', 'widget:read'];
  liveFails = false;
  bridgeCount = 1;
  required = [];

  connection = {
    isEnabled: true,
    mode: 'direct',
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

let queryClient = new QueryClient();

// The page opens on its Live tab; a test names another tab the way the URL does (?view=).
async function open(view?: 'live' | 'tools' | 'overlays', hash = ''): Promise<void> {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/obs', component: { template: '<div />' } }],
  });

  await router.push(`${view
    ? `/app/obs?view=${view}`
    : '/app/obs'}${hash}`);

  mounted = mount(ObsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        router,
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
  it('shows each browser source as green loaded, orange not loaded, red failed or gray off, with its reason', async () => {
    await open('overlays');

    const rows = Array.from(document.querySelectorAll('[data-obs-source]'));

    expect(
      rows.map((row) => row.querySelector('.dot')?.className.replace('dot', '').trim()),
    ).toEqual(['ok', 'warn', 'bad', '']);

    expect(rows[0]?.textContent).toContain(en.dashboard.obs.sources.loaded);
    expect(rows[1]?.textContent).toContain(en.dashboard.obs.sources.notLoaded);
    expect(rows[2]?.textContent).toContain('TypeError: feather is undefined');
    expect(rows[3]?.textContent).toContain(en.dashboard.obs.sources.off);
  });

  it('marks the connection green and lists the scenes with the live one on the same first view', async () => {
    await open();

    expect(dotsOf('connection')).toEqual(['dot ok']);
    expect(dotsOf('bridge')).toEqual(['dot ok']);
    expect(document.querySelector('[data-obs-connection]')?.textContent).toContain('localhost:4455');

    const scenes = Array.from(document.querySelectorAll('[data-obs-scene]'));

    expect(scenes.map((scene) => scene.getAttribute('data-current'))).toEqual(['true', 'false']);
  });

  // Stoney 2026-10-15: "the current page does not even react to scene changes so i expect better".
  it('moves the live scene mark when OBS switches scene, without reading the scenes again', async () => {
    await open();
    calls.length = 0;

    obsStateChanged.handle(
      { queries: queryClient, channelId: 'c1' },
      {
        broadcasterId: 'c1',
        kind: 'programScene',
        sceneName: 'BRB',
        timestamp: '2026-10-15T20:00:00Z',
      },
    );

    await flushPromises();

    const scenes = Array.from(document.querySelectorAll('[data-obs-scene]'));

    expect(scenes.map((scene) => scene.getAttribute('data-current'))).toEqual(['false', 'true']);
    expect(calls.filter((path) => path.endsWith('/obs/scenes'))).toEqual([]);
  });

  it('turns the connection red with the bot reason and the header dot red when OBS reports an error', async () => {
    connection = { ...connection, lastError: 'Authentication failed' };
    await open();

    expect(dotsOf('connection')).toEqual(['dot bad']);

    expect(document.querySelector('[data-obs-connection]')?.textContent).toContain(
      'Authentication failed',
    );

    expect(document.querySelector('[data-obs-overall] .dot')?.className).toContain('bad');
  });

  it('turns the connection orange when OBS is linked but refuses the live reads', async () => {
    liveFails = true;
    await open();

    expect(dotsOf('connection')).toEqual(['dot warn']);
  });

  // Stoney 2026-10-16: OBS connected, no bridge page open, the header said "Something is not loaded".
  it('keeps the connection green and the bridge gray when OBS is direct and no bridge page is open', async () => {
    bridgeCount = 0;
    widgets = [widget('w1', 'Chat', { isAttached: true })];
    await open();

    expect(dotsOf('connection')).toEqual(['dot ok']);
    expect(dotsOf('bridge')).toEqual(['dot']);
    expect(document.querySelector('[data-obs-overall] .dot')?.className).toContain('ok');

    expect(document.querySelector('[data-obs-overall]')?.textContent).toContain(
      en.dashboard.obs.overall.ok,
    );

    expect(document.querySelector('[data-obs-connection]')?.textContent).toContain(
      en.dashboard.obs.bridge.optional,
    );
  });

  // Stoney 2026-10-21 (mobile ledger Z-02): "Something is not loaded" never said what.
  it('names the browser sources that are not loaded in the header words', async () => {
    widgets = [
      widget('w1', 'Chat', { isAttached: true }),
      widget('w2', 'Alerts', { isAttached: false }),
      widget('w3', 'Goals', { isAttached: false }),
    ];

    await open();

    const words = document.querySelector('[data-obs-overall]')?.textContent ?? '';

    expect(document.querySelector('[data-obs-overall] .dot')?.className).toContain('warn');
    expect(words).toContain('2 browser sources are not loaded in OBS');
    expect(words).not.toContain(en.dashboard.obs.overall.warn);
  });

  it('names the failed browser sources in the header words', async () => {
    widgets = [widget('w1', 'Chat', { isAttached: true, lastRuntimeError: 'boom' })];
    await open();

    expect(document.querySelector('[data-obs-overall]')?.textContent).toContain(
      '1 browser source failed on its last run',
    );
  });

  it('still turns the bridge orange when OBS is driven through the bridge and no page is open', async () => {
    bridgeCount = 0;
    connection = { ...connection, mode: 'bridge' };
    await open();

    expect(dotsOf('bridge')).toEqual(['dot warn']);
  });

  it('reads no scenes or state while the OBS link is switched off', async () => {
    connection = { ...connection, isEnabled: false };
    await open();

    expect(calls.some((path) => path.endsWith('/obs/scenes') || path.endsWith('/obs/state'))).toBe(
      false,
    );

    expect(dotsOf('connection')).toEqual(['dot']);
    expect(document.body.textContent).toContain(en.dashboard.obs.scenes.offline);
  });

  it('hides the connection panel from a person who may control OBS but not read its configuration', async () => {
    held = ['obs:scene:read', 'obs:control'];
    await open();

    expect(document.querySelector('[data-obs-connection]')).toBeNull();
    expect(document.querySelectorAll('[data-obs-scene]')).toHaveLength(2);
  });

  it('runs the moderator panels on the status read, and never asks for the configuration or the bridge', async () => {
    held = ['obs:scene:read', 'obs:control'];
    await open();

    const paths = calls.filter((path) => path.includes('/obs/'));

    expect(paths).toContainEqual(expect.stringContaining('/obs/connection?view=Status'));
    expect(paths.filter((path) => path.endsWith('/obs/connection'))).toEqual([]);
    expect(paths.filter((path) => path.endsWith('/obs/bridge/status'))).toEqual([]);
    expect(document.body.textContent).not.toContain(en.dashboard.obs.scenes.offline);
    expect(paths).toContainEqual(expect.stringContaining('/obs/state'));
    expect(document.querySelectorAll('[data-obs-scene]')).toHaveLength(2);
  });

  it('shows a required overlay only in the Required overlays panel, never in the browser sources', async () => {
    required = [{ key: 'audio', widget: widgets[3] }];
    await open('overlays');

    const panelRows = Array.from(document.querySelectorAll('[data-required-overlay]'));
    const sourceRows = Array.from(document.querySelectorAll('[data-obs-source]'));

    expect(panelRows.map((row) => row.getAttribute('data-required-overlay'))).toEqual(['audio']);
    expect(sourceRows).toHaveLength(3);
    expect(sourceRows.map((row) => row.textContent).join(' ')).not.toContain('Audio');
  });

  it('hides the Required overlays panel and never reads the list without widget:read', async () => {
    held = ['obs:scene:read', 'obs:control', 'obs:config:read'];
    required = [{ key: 'audio', widget: widgets[3] }];
    await open();

    expect(document.querySelector('#required-overlays')).toBeNull();
    expect(calls.some((path) => path.endsWith('/overlays/required'))).toBe(false);
  });

  it('shows the connection panel to a person who holds the configuration key', async () => {
    held = ['obs:scene:read', 'obs:control', 'obs:config:read'];
    await open();

    expect(document.querySelector('[data-obs-connection]')).not.toBeNull();
  });

  it('shows a moderator the board with every write control disabled and the reason on the board', async () => {
    held = ['obs:scene:read'];
    await open();

    const scope = document
      .querySelector('[data-obs-board]')
      ?.closest('[data-slot="read-only-scope"]');

    expect(scope?.hasAttribute('disabled')).toBe(true);
    expect(scope?.getAttribute('title')).toBe(en.dashboard.obs.needsControl);
    expect(document.querySelectorAll('[data-obs-scene]')).toHaveLength(2);

    expect(document.querySelector('[data-obs-readonly-note]')?.textContent).toContain(
      en.dashboard.obs.needsControl,
    );
  });

  it('leaves the board enabled for an editor who holds obs:control', async () => {
    held = ['obs:scene:read', 'obs:control'];
    await open();

    const scope = document
      .querySelector('[data-obs-board]')
      ?.closest('[data-slot="read-only-scope"]');

    expect(scope?.hasAttribute('disabled')).toBe(false);
    expect(document.querySelector('[data-obs-readonly-note]')).toBeNull();
  });

  it('follows obs:scene:read for the board, not obs:control', async () => {
    held = ['obs:control'];
    await open();

    expect(document.querySelector('[data-obs-board]')).toBeNull();
    expect(document.querySelectorAll('[data-obs-scene]')).toHaveLength(0);
  });

  // Stoney: "the settings are hidden in a hidden dropdown all the way down, this page needs a proper reorganization".
  it('shows the connection first, above the live board, with no folded setup section', async () => {
    await open();

    const bar = document.querySelector('[data-obs-connection]');
    const board = document.querySelector('[data-obs-board]');

    expect(bar).not.toBeNull();
    expect(board).not.toBeNull();
    expect(document.querySelector('[data-obs-setup]')).toBeNull();
    expect(bar!.compareDocumentPosition(board!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('places each live panel in its own bento area, the outputs panel included', async () => {
    await open();

    const areas = [...document.querySelectorAll('[data-obs-live] > *')]
      .map((panel) => /\[grid-area:(\w+)\]/.exec(panel.className)?.[1]);

    expect(areas).toEqual(['outputs', 'scenes', 'audio', 'sources']);
  });

  it('opens the saved connection in a dialog from Edit connection', async () => {
    held = [...held, 'obs:config:write'];
    await open();

    expect(document.querySelector('[data-obs-host]')).toBeNull();

    document.querySelector<HTMLButtonElement>('[data-obs-edit-connection]')?.click();
    await flushPromises();

    const host = document.querySelector<HTMLInputElement>('[role="dialog"] [data-obs-host]');

    expect(host?.value).toBe('localhost');
  });

  it('offers no Edit connection to a person who may read the connection but not change it', async () => {
    await open();

    expect(document.querySelector('[data-obs-connection]')).not.toBeNull();
    expect(document.querySelector('[data-obs-edit-connection]')).toBeNull();
  });

  it('makes Edit connection the one primary action only while OBS is not linked', async () => {
    held = [...held, 'obs:config:write'];
    await open();

    expect(document.querySelector('[data-obs-edit-connection]')?.getAttribute('data-variant')).toBe('outline');

    mounted?.unmount();
    connection = { ...connection, isEnabled: false };
    await open();

    expect(document.querySelector('[data-obs-edit-connection]')?.getAttribute('data-variant')).toBe('default');
  });

  // Stoney 2026-10-18: "can you slap more things on in pannels that large?! how is that managible by the streamer while live?"
  it('keeps only the live controls on the Live tab, and the tools and overlays on their own tabs', async () => {
    required = [{ key: 'audio', widget: widgets[3] }];
    await open();

    expect(document.querySelectorAll('[data-output]')).toHaveLength(4);
    expect(document.querySelectorAll('[data-obs-scene]')).toHaveLength(2);
    expect(document.querySelector('[data-obs-shot-source]')).toBeNull();
    expect(document.querySelector('[data-required-overlays]')).toBeNull();
    expect(document.querySelector('[data-obs-source]')).toBeNull();

    mounted?.unmount();
    await open('tools');

    expect(document.querySelector('[data-obs-shot-source]')).not.toBeNull();
    expect(document.querySelector('[data-output]')).toBeNull();

    mounted?.unmount();
    await open('overlays');

    expect(document.querySelector('[data-required-overlays]')).not.toBeNull();
    expect(document.querySelector('[data-output]')).toBeNull();
    expect(document.querySelector('[data-obs-connection]')).not.toBeNull();
  });

  // Stoney 2026-10-22: "setup in obs does not redirect to the actual setup"
  it('scrolls the required overlays panel into view when the link carries its anchor', async () => {
    const scrolled = vi.fn();

    Element.prototype.scrollIntoView = scrolled;
    required = [{ key: 'audio', widget: widgets[3] }];
    await open('overlays', '#required-overlays');
    await flushPromises();

    const toTop = scrolled.mock.calls.flatMap((call, index) =>
      call[0]?.block === 'start'
        ? [scrolled.mock.contexts[index]]
        : []);

    expect(toTop).toEqual([document.getElementById('required-overlays')]);
  });

  it('does not scroll when the link has no anchor', async () => {
    const scrolled = vi.fn();

    Element.prototype.scrollIntoView = scrolled;
    await open('overlays');
    await flushPromises();

    expect(scrolled.mock.calls.filter((call) => call[0]?.block === 'start')).toEqual([]);
  });

  it('shows only the tabs a person may open', async () => {
    held = ['obs:scene:read', 'obs:control'];
    await open();

    const tabs = Array.from(document.querySelectorAll('[role="tab"]')).map((tab) => tab.textContent?.trim());

    expect(tabs).toEqual([en.dashboard.obs.tabs.live, en.dashboard.obs.tabs.tools]);
  });
});
