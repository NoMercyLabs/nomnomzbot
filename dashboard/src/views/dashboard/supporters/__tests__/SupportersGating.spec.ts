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
import SupportersView from '../SupportersView.vue';

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

vi.mock('@/components/layout/UserName.vue', () => ({
  default: { template: '<span />' },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: { data: [] },
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

  mounted = mount(SupportersView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

const titles = (view: VueWrapper): string => view.text();

describe('SupportersView shows only what the person may use', () => {
  it('shows no panel without any key', async () => {
    const view = await open([]);

    expect(titles(view)).not.toContain(en.dashboard.supporters.subs.title);
    expect(titles(view)).not.toContain(en.dashboard.supporters.cheers.title);
    expect(titles(view)).not.toContain(en.dashboard.supporters.top.title);
    expect(titles(view)).not.toContain(en.dashboard.supporters.tips.title);
    expect(view.find('[data-view="supporter-connections"]').exists()).toBe(false);
  });

  it('shows the Twitch panels with dashboard:read and nothing else', async () => {
    const view = await open(['dashboard:read']);

    expect(titles(view)).toContain(en.dashboard.supporters.subs.title);
    expect(titles(view)).toContain(en.dashboard.supporters.cheers.title);
    expect(titles(view)).toContain(en.dashboard.supporters.top.title);
    expect(titles(view)).not.toContain(en.dashboard.supporters.tips.title);
    expect(view.find('[data-view="supporter-connections"]').exists()).toBe(false);
  });

  it('shows the tips panel with supporters:read and never holds the connection form', async () => {
    const reader = await open(['supporters:read']);

    expect(titles(reader)).toContain(en.dashboard.supporters.tips.title);
    expect(reader.find('[data-view="supporter-connections"]').exists()).toBe(false);

    reader.unmount();
    mounted = null;

    const writer = await open(['supporters:read', 'supporters:config:write']);

    expect(writer.find('[data-view="supporter-connections"]').exists()).toBe(false);
    expect(writer.find('form').exists()).toBe(false);
  });
});
