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

async function openSettings(): Promise<void> {
  document.body.querySelector<HTMLButtonElement>('[data-action="open-settings"]')?.click();
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
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

    await openSettings();

    const fields = [...document.body.querySelectorAll<HTMLInputElement>('input[data-field]')];

    expect(fields.length).toBeGreaterThan(0);
    expect(fields.every((field) => field.disabled)).toBe(true);
    expect(document.body.querySelector('[data-action="discard"]')).toBeNull();
  });

  it('shows the queue actions with media:moderate and the save row with media:write', async () => {
    const view = await open(['media:read', 'media:moderate', 'media:write']);

    expect(view.find('[data-action="approve"]').exists()).toBe(true);

    await openSettings();

    const fields = [...document.body.querySelectorAll<HTMLInputElement>('input[data-field]')];

    expect(document.body.querySelector('[data-action="discard"]')).not.toBeNull();
    expect(fields.every((field) => !field.disabled)).toBe(true);
  });
});
