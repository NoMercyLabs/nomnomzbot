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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import PipelinesView from '../PipelinesView.vue';

const row = {
  id: 'p1',
  name: 'Roast',
  description: 'Roasts a viewer',
  isEnabled: true,
  hasPlatformDefault: false,
  triggerCount: 4,
  lastTriggeredAt: null,
};

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

vi.mock('@/api/pipelines', async (original) => ({
  ...(await original<typeof import('@/api/pipelines')>()),
  usePipelines: () => ({
    data: ref([row]),
    isPending: ref(false),
    isError: ref(false),
    refetch: () => undefined,
  }),
  useCreatePipeline: () => ({ mutateAsync: async () => row, isPending: ref(false) }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(keys: string[]): Promise<VueWrapper> {
  held.keys = keys;

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/pipelines',
        name: 'dashboard-pipelines',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push('/app/pipelines');

  mounted = mount(PipelinesView, {
    attachTo: document.body,
    global: { plugins: [i18n, router] },
  });

  await flushPromises();

  return mounted;
}

describe('PipelinesView shows only what the person may use', () => {
  function newButton(): HTMLButtonElement | null {
    return document.body.querySelector<HTMLButtonElement>('[data-pipelines-new]');
  }

  it('shows no list and a New that is in place but off without any key', async () => {
    const view = await open([]);

    expect(view.find('[data-pipeline-row]').exists()).toBe(false);
    expect(newButton()?.disabled).toBe(true);
  });

  it('shows the list and a New that is in place but off with pipelines:read alone', async () => {
    const view = await open(['pipelines:read']);

    expect(view.find('[data-pipeline-row]').exists()).toBe(true);
    expect(newButton()?.disabled).toBe(true);
  });

  it('turns New on with pipelines:write', async () => {
    await open(['pipelines:read', 'pipelines:write']);

    expect(newButton()?.disabled).toBe(false);
  });
});
