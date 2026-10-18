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

import en from '../i18n/en.json';
import ObsScreenshotPanel from '../components/ObsScreenshotPanel.vue';

type Post = { path: string; body: unknown };

const posts: Post[] = [];

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({ data: { data: [] }, response: { status: 200, ok: true } }),
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return { data: { data: 'data:image/jpeg;base64,AAAA' }, response: { status: 200, ok: true } };
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

function show(): VueWrapper {
  mounted = mount(ObsScreenshotPanel, {
    attachTo: document.body,
    global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
    props: { offline: false },
  });

  return mounted;
}

describe('ObsScreenshotPanel', () => {
  it('captures the named source in the chosen format and shows the returned image', async () => {
    const view = show();

    await view.get('[data-obs-shot-source]').setValue('Camera');
    await view.get('[data-obs-shot-format="jpg"]').trigger('click');
    await view.get('[data-obs-shot-capture]').trigger('click');
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/source-screenshot',
        body: { sourceName: 'Camera', imageFormat: 'jpg' },
      },
    ]);

    expect(view.get('[data-obs-shot-image]').attributes('src')).toBe('data:image/jpeg;base64,AAAA');
  });

  it('does not call OBS without a source name', async () => {
    const view = show();

    await view.get('[data-obs-shot-capture]').trigger('click');
    await flushPromises();

    expect(posts).toHaveLength(0);
  });
});
