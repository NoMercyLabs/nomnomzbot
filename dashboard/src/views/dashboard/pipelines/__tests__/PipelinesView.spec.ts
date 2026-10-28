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
import { computed, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import type { PipelineListItem } from '@/api/pipelines';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import PipelinesView from '../PipelinesView.vue';

const rows = [
  {
    id: 'p2',
    name: 'Shoutout',
    description: null,
    isEnabled: false,
    hasPlatformDefault: true,
    triggerCount: 0,
    lastTriggeredAt: null,
  },
  {
    id: 'p1',
    name: 'Roast',
    description: 'Roasts a viewer',
    isEnabled: true,
    hasPlatformDefault: false,
    triggerCount: 4,
    lastTriggeredAt: null,
  },
] as unknown as PipelineListItem[];

const held = ref<string[]>([]);
const create = vi.hoisted(() => ({
  mutateAsync: vi.fn<(change: { name: string }) => Promise<{ id: string; name: string }>>(),
}));

vi.mock('@/api/pipelines', async (original) => ({
  ...(await original<typeof import('@/api/pipelines')>()),
  usePipelines: () => ({
    data: ref(rows),
    isPending: ref(false),
    isError: ref(false),
    refetch: vi.fn<() => void>(),
  }),
  useCreatePipeline: () => ({ mutateAsync: create.mutateAsync, isPending: ref(false) }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => true),
    can: (key: string) => held.value.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let router: Router;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  held.value = ['pipelines:read', 'pipelines:write'];
  create.mutateAsync.mockReset();
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/pipelines',
        name: 'dashboard-pipelines',
        component: blank,
      },
      {
        path: '/app/pipelines/:id',
        name: 'dashboard-pipeline',
        component: blank,
      },
    ],
  });

  await router.push('/app/pipelines');
  mounted = mount(PipelinesView, { attachTo: document.body, global: { plugins: [i18n, router] } });
  await flushPromises();
}

const listed = (): string[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-pipeline-row]')].map(
    (row) => row.dataset.pipelineId ?? '',
  );
const newButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-pipelines-new]')!;

describe('the pipelines list', () => {
  it('lists every pipeline by name and marks the ready-made and the switched-off one', async () => {
    await open();

    expect(listed()).toEqual(['p1', 'p2']);

    const shoutout = document.body.querySelector<HTMLElement>('[data-pipeline-id="p2"]')!;

    expect(shoutout.textContent).toContain('Ready-made');
    expect(shoutout.textContent).toContain('Off');

    expect(document.body.querySelector('[data-pipeline-id="p1"]')?.textContent).toContain(
      'Roasts a viewer',
    );
  });

  // Mobile ledger Z-01: rows were plain text; the page scrolled inside the panel.
  it('ends every row with a chevron, clamps the description to two lines and lets the page scroll on a phone', async () => {
    await open();

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-pipeline-row]')];

    expect(rows).toHaveLength(2);

    for (const row of rows) {
      expect(row.querySelector('[data-slot="summary-row-chevron"]')).not.toBeNull();
      expect(row.className).toContain('line-clamp-2');
    }

    const body = document.body.querySelector<HTMLElement>('[data-page-body]')!;

    expect(body.className).toContain('max-md:overflow-y-auto');
    expect(body.className).toContain('max-md:*:shrink-0');
  });

  it('filters by name and description as the person types', async () => {
    await open();
    await mounted!.find('input').setValue('viewer');

    expect(listed()).toEqual(['p1']);
  });

  it('opens the workbench of the picked pipeline', async () => {
    await open();
    document.body.querySelector<HTMLElement>('[data-pipeline-id="p1"]')!.click();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines/p1');
  });

  it('keeps New in place but off for a role that cannot change pipelines', async () => {
    held.value = ['pipelines:read'];
    await open();

    expect(document.body.querySelector<HTMLButtonElement>('[data-pipelines-new]')?.disabled).toBe(true);
  });

  it('makes a pipeline from the typed name and opens its workbench', async () => {
    create.mutateAsync.mockResolvedValue({ id: 'p9', name: 'Hug' });
    await open();
    newButton().click();
    await flushPromises();

    const name = document.body.querySelector<HTMLInputElement>('[data-pipelines-new-name]')!;

    name.value = '  Hug ';
    name.dispatchEvent(new Event('input'));
    await flushPromises();

    [...document.body.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Make pipeline')!
      .click();

    await flushPromises();

    expect(create.mutateAsync).toHaveBeenCalledWith({ name: 'Hug' });
    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines/p9');
    expect(document.body.querySelector('[data-pipelines-new-dialog]')).toBeNull();
  });

  it('keeps the dialog open with the bot\'s reason when it refuses', async () => {
    create.mutateAsync.mockRejectedValue(new Error('A pipeline with this name already exists.'));
    await open();
    newButton().click();
    await flushPromises();

    const name = document.body.querySelector<HTMLInputElement>('[data-pipelines-new-name]')!;

    name.value = 'Roast';
    name.dispatchEvent(new Event('input'));
    await flushPromises();

    [...document.body.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.trim() === 'Make pipeline')!
      .click();

    await flushPromises();

    expect(document.body.querySelector('[data-pipelines-new-dialog]')?.textContent).toContain(
      'A pipeline with this name already exists.',
    );

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines');
  });
});
