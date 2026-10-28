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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { ObsFilter, ObsSceneItem } from '@/api/obsSources';
import { i18n } from '@/i18n';

import SourcesPanel from '../components/SourcesPanel.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: { data: ObsSceneItem[] | ObsFilter[] | object };
  error?: { detail: string };
  response: { status: number; ok: boolean };
};

type Post = { path: string; body: object };

const posts: Post[] = [];
const toastError = vi.fn<(message: string) => void>();
let items: ObsSceneItem[] = [];
let filters: ObsFilter[] = [];
let postFails = false;

function apply(path: string, body: object): void {
  if (path.endsWith('/source-filters/enabled')) {
    const change = body as { filterName: string; enabled: boolean };

    filters = filters.map((filter) =>
      filter.name === change.filterName
        ? {
            ...filter,
            enabled: change.enabled,
          }
        : filter,
    );

    return;
  }

  const change = body as Partial<ObsSceneItem>;

  items = items.map((item) =>
    item.sceneItemId === change.sceneItemId
      ? {
          ...item,
          ...change,
        }
      : item,
  );
}

const ok = (data: ObsSceneItem[] | ObsFilter[] | object): Answer => ({
  data: { data },
  response: { status: 200, ok: true },
});

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('vue-sonner', () => ({ toast: { error: (message: string) => toastError(message) } }));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> =>
      path.endsWith('/source-filters')
        ? ok(filters)
        : ok(items),
    POST: async (path: string, init: { body: object }): Promise<Answer> => {
      posts.push({ path, body: init.body });

      if (postFails)
        return { error: { detail: 'OBS refused' }, response: { status: 502, ok: false } };

      // OBS keeps the change, so the refetch after the write reads it back.
      apply(path, init.body);

      return ok({});
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  toastError.mockReset();
  postFails = false;

  items = [
    {
      sceneItemId: 1,
      sourceName: 'Webcam',
      enabled: true,
      locked: false,
    },
    {
      sceneItemId: 2,
      sourceName: 'Overlay',
      enabled: false,
      locked: true,
    },
  ];

  filters = [
    {
      name: 'Color',
      kind: 'color_filter',
      enabled: true,
      index: 0,
    },
  ];

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(sceneName: string | null = 'Main'): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(SourcesPanel, {
    attachTo: document.body,
    props: { sceneName, offline: false },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();
}

function row(name: string): HTMLElement {
  const found = document.querySelector<HTMLElement>(`[data-obs-scene-item][data-source="${name}"]`);

  if (!found)
    throw new Error(`no source row ${name}`);

  return found;
}

const pressed = (name: string, control: string): string | null =>
  row(name).querySelector(`[${control}]`)?.getAttribute('aria-pressed') ?? null;

function click(name: string, control: string): void {
  row(name).querySelector<HTMLElement>(`[${control}]`)?.click();
}

describe('OBS sources panel', () => {
  it('lists the items of the scene on air with their shown and locked flags', async () => {
    await open();

    expect(pressed('Webcam', 'data-source-shown')).toBe('true');
    expect(pressed('Webcam', 'data-source-locked')).toBe('false');
    expect(pressed('Overlay', 'data-source-shown')).toBe('false');
    expect(pressed('Overlay', 'data-source-locked')).toBe('true');
  });

  it('hides an item by its id and shows it hidden at once', async () => {
    await open();

    click('Webcam', 'data-source-shown');
    await flushPromises();

    expect(posts[0]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/scene-items/enabled',
      body: {
        sceneName: 'Main',
        sceneItemId: 1,
        enabled: false,
      },
    });

    expect(pressed('Webcam', 'data-source-shown')).toBe('false');
  });

  it('locks an item and rolls the lock back with a toast when OBS refuses', async () => {
    postFails = true;
    await open();

    click('Webcam', 'data-source-locked');
    await settle();

    expect(posts[0]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/scene-items/locked',
      body: {
        sceneName: 'Main',
        sceneItemId: 1,
        locked: true,
      },
    });

    expect(pressed('Webcam', 'data-source-locked')).toBe('false');
    expect(toastError).toHaveBeenCalledOnce();
  });

  it('opens a source to switch one of its filters off', async () => {
    await open();

    click('Webcam', 'data-source-filters-toggle');
    await settle();

    const toggle = document.querySelector<HTMLElement>('[data-source-filter-enabled]');

    expect(toggle?.getAttribute('aria-checked')).toBe('true');

    toggle?.click();
    await flushPromises();

    expect(posts[0]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/source-filters/enabled',
      body: {
        sourceName: 'Webcam',
        filterName: 'Color',
        enabled: false,
      },
    });

    expect(toggle?.getAttribute('aria-checked')).toBe('false');
  });

  it('says so instead of an empty list while no scene is on air', async () => {
    await open(null);

    expect(document.querySelector('[data-obs-scene-item]')).toBeNull();
    expect(document.body.textContent).toContain(en.dashboard.obs.sourceItems.offline);
  });
});
