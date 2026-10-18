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
import MediaShareView from '../MediaShareView.vue';

const held = vi.hoisted(() => ({ keys: [] as string[] }));

const config = {
  isEnabled: true,
  requireApproval: true,
  allowTwitchClips: true,
  allowYouTube: false,
  maxDurationSeconds: 60,
  entryCost: 0,
  maxQueueLength: 10,
  perUserCooldownSeconds: 30,
};

const pending = {
  id: 'r1',
  requesterDisplayName: 'viewer-r1',
  sourceType: 'twitch_clip',
  sourceUrl: 'https://clips.example/r1',
  title: 'Clip r1',
  status: 'pending',
  requestedAt: '2026-10-01T10:00:00Z',
};

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
        data: path.endsWith('/config')
          ? config
          : [pending],
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

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(MediaShareView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('MediaShareView shows only what the person may use', () => {
  it('shows no queue and no settings without media:read', async () => {
    const view = await open([]);

    expect(view.text()).not.toContain(en.dashboard.mediaShare.queue.title);
    expect(view.find('[data-field]').exists()).toBe(false);
  });

  it('shows the queue and settings read-only with media:read alone', async () => {
    const view = await open(['media:read']);

    expect(view.text()).toContain(en.dashboard.mediaShare.queue.title);
    expect(view.text()).toContain('Clip r1');
    expect(view.find('[data-media-share-row]').exists()).toBe(true);
    expect(view.find('[data-action="approve"]').exists()).toBe(false);

    const fields = view.findAll('input[data-field]');

    expect(fields.length).toBeGreaterThan(0);
    expect(fields.every((field) => field.attributes('disabled') !== undefined)).toBe(true);
    expect(view.find('[data-action="discard"]').exists()).toBe(false);
  });

  it('shows the queue actions with media:moderate and the save row with media:write', async () => {
    const view = await open(['media:read', 'media:moderate', 'media:write']);

    expect(view.find('[data-action="approve"]').exists()).toBe(true);
    expect(view.find('[data-action="discard"]').exists()).toBe(true);

    const fields = view.findAll('input[data-field]');

    expect(fields.every((field) => field.attributes('disabled') === undefined)).toBe(true);
  });
});
