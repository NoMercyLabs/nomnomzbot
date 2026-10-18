// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
    (row: HTMLElement) => row.dataset.pipelineId ?? '',
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

  it('hides New for a role that cannot change pipelines', async () => {
    held.value = ['pipelines:read'];
    await open();

    expect(document.body.querySelector('[data-pipelines-new]')).toBeNull();
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
      .find((button: HTMLButtonElement) => button.textContent?.trim() === 'Make pipeline')!
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
      .find((button: HTMLButtonElement) => button.textContent?.trim() === 'Make pipeline')!
      .click();

    await flushPromises();

    expect(document.body.querySelector('[data-pipelines-new-dialog]')?.textContent).toContain(
      'A pipeline with this name already exists.',
    );

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines');
  });
});
