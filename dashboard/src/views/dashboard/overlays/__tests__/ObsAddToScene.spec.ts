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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ObsAddToScene from '../components/ObsAddToScene.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type Post = { path: string; body: unknown };

const WIDGET_ID = '0192a000-0000-7000-8000-00000000c0b0';
const posts: Post[] = [];
let held: string[] = [];
let enabled = true;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    POST: async (
      path: string,
      options?: { body?: { overlayKeys?: string[] } },
    ): Promise<Answer> => {
      posts.push({ path, body: options?.body });

      return {
        data: {
          data: {
            nestedSceneName: null,
            addedToScene: true,
            overlays: (options?.body?.overlayKeys ?? []).map((key) => ({
              key,
              sourceName: 'Chat Box',
              outcome: 'Created',
              error: null,
            })),
          },
        },
        response: { status: 200, ok: true },
      };
    },
    GET: async (path: string): Promise<Answer> => {
      if (path.endsWith('/obs/connection'))
        return { data: { data: { mode: 'direct', isEnabled: enabled } }, response: { status: 200, ok: true } };

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

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (key: string): boolean => held.includes(key) }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  held = ['obs:control'];
  enabled = true;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function show(): Promise<VueWrapper> {
  mounted = mount(ObsAddToScene, {
    attachTo: document.body,
    props: { placeKey: WIDGET_ID, name: 'Chat Box' },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  await flushPromises();

  return mounted;
}

describe('ObsAddToScene', () => {
  it('adds the widget to the scene on air under the widget name, keyed by the widget id', async () => {
    const view = await show();

    expect((view.find('[data-obs-add-name]').element as HTMLInputElement).value).toBe('Chat Box');

    await view.find('[data-obs-add-submit]').trigger('click');
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/overlays/install',
        body: {
          sceneName: 'Main',
          overlayKeys: [WIDGET_ID],
          placements: [{
            key: WIDGET_ID,
            sceneName: 'Main',
            sourceName: 'Chat Box',
          }],
        },
      },
    ]);

    expect(view.emitted('placed')).toHaveLength(1);
  });

  it('sends the name the person typed instead of the widget name', async () => {
    const view = await show();

    await view.find('[data-obs-add-name]').setValue('My chat');
    await view.find('[data-obs-add-submit]').trigger('click');
    await flushPromises();

    expect(posts[0]?.body).toMatchObject({ placements: [{ sourceName: 'My chat' }] });
  });

  it('stays hidden and sends nothing when the person may not control OBS', async () => {
    held = [];
    const view = await show();

    expect(view.find('[data-obs-add-to-scene]').exists()).toBe(false);
    expect(view.emitted('update:shown')).toBeUndefined();
  });

  it('stays hidden while the OBS connection is switched off', async () => {
    enabled = false;
    const view = await show();

    expect(view.find('[data-obs-add-to-scene]').exists()).toBe(false);
  });
});
