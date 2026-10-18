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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ObsHotkeysPanel from '../components/ObsHotkeysPanel.vue';
import en from '../i18n/en.json';

type Post = { path: string; body: unknown };

const posts: Post[] = [];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: { data: ['OBSBasic.StartStreaming', 'OBSBasic.StartRecording'] },
      response: { status: 200, ok: true },
    }),
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('ObsHotkeysPanel', () => {
  it('lists the hotkeys OBS knows and fires the one clicked by its name', async () => {
    mounted = mount(ObsHotkeysPanel, {
      attachTo: document.body,
      global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
      props: { offline: false },
    });

    await flushPromises();

    const buttons = mounted.findAll('[data-obs-hotkey]');

    expect(buttons.map((button) => button.attributes('data-obs-hotkey'))).toEqual([
      'OBSBasic.StartStreaming',
      'OBSBasic.StartRecording',
    ]);

    await buttons[1]?.trigger('click');
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/hotkeys/trigger',
        body: { hotkeyName: 'OBSBasic.StartRecording' },
      },
    ]);
  });
});
