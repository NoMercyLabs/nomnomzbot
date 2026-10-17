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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import TimersView from '../TimersView.vue';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  id: unknown;
  body: Record<string, unknown> | undefined;
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let rows: Row[] = [];
let refusePatch: string | null = null;

const trigger = (id: string, overrides: Row = {}): Row => ({
  id,
  pattern: `words ${id}`,
  matchType: 'contains',
  caseSensitive: false,
  isEnabled: true,
  response: 'Hello there',
  pipelineId: null,
  cooldownSeconds: 30,
  minPermissionLevel: 'Everyone',
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  ...overrides,
});

type Answer = { response: { status: number; ok: boolean }; error?: unknown };

function write(method: string, options?: Options): Answer {
  const id = options?.params?.path?.triggerId;

  calls.push({
    method,
    id,
    body: options?.body,
  });

  if (method === 'PATCH' && refusePatch)
    return { error: { message: refusePatch }, response: { status: 400, ok: false } };

  if (method === 'PATCH') {
    rows = rows.map((row) =>
      row.id === id
        ? { ...row, ...options?.body }
        : row);
  }

  if (method === 'DELETE') {
    rows = rows.filter((row) => row.id !== id);
  }

  if (method === 'POST') {
    rows.push(trigger('new', options?.body));
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => ({
      data: { data: path.endsWith('/chat-triggers')
        ? rows
        : [] },
      response: { status: 200, ok: true },
    }),
    POST: async (_path: string, options?: Options) => write('POST', options),
    PATCH: async (_path: string, options?: Options) => write('PATCH', options),
    DELETE: async (_path: string, options?: Options) => write('DELETE', options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  rows = [trigger('a'), trigger('b', { isEnabled: false })];
  refusePatch = null;
  pinia = createPinia();
  setActivePinia(pinia);
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

async function open(): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/triggers-timers', component: { template: '<div />' } }],
  });

  await router.push('/app/triggers-timers?view=triggers');

  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(TimersView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const patterns = (): (string | undefined)[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-trigger-row]')].map(
    (row) => row.dataset.triggerPattern,
  );
const rowOf = (pattern: string): HTMLElement =>
  document.body.querySelector(`[data-trigger-pattern="${pattern}"]`) as HTMLElement;
const buttonByText = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
const field = (label: string): HTMLInputElement =>
  document.body.querySelector(`[aria-label="${label}"]`) as HTMLInputElement;

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

describe('Chat triggers tab', () => {
  it('lists the triggers of the channel', async () => {
    await open();

    expect(patterns()).toEqual(['words a', 'words b']);
  });

  it('sends only the changed fields in a PATCH and shows the saved value', async () => {
    await open();
    await click(rowOf('words a'));
    await type(field('Seconds between replies'), '90');
    await click(buttonByText('Save changes'));

    expect(calls.filter((call) => call.method === 'PATCH')).toEqual([
      {
        method: 'PATCH',
        id: 'a',
        body: { cooldownSeconds: 90 },
      },
    ]);
  });

  it('keeps the draft and shows the reason when the bot refuses the save', async () => {
    refusePatch = 'Pattern too long';
    await open();
    await click(rowOf('words a'));
    await type(field('Words to watch for'), 'changed');
    await click(buttonByText('Save changes'));

    expect(document.body.querySelector('[data-trigger-editor] [role="alert"]')?.textContent).toContain(
      'Pattern too long',
    );

    expect(field('Words to watch for').value).toBe('changed');
  });

  it('creates a trigger from the dialog and refetches the list', async () => {
    await open();
    await click(buttonByText('New trigger'));
    await type(field('Words to watch for'), 'hello bot');
    await type(field('Reply'), 'Hi!');
    await click(buttonByText('Create trigger'));

    expect(calls.filter((call) => call.method === 'POST')[0]!.body).toEqual({
      pattern: 'hello bot',
      response: 'Hi!',
    });

    expect(patterns()).toContain('hello bot');
  });

  it('deletes a trigger only after the confirm and refetches the list', async () => {
    await open();
    await click(rowOf('words a'));
    await click(buttonByText('Delete'));

    expect(calls.filter((call) => call.method === 'DELETE')).toHaveLength(0);

    await click(buttonByText('Delete trigger'));

    expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.id)).toEqual(['a']);
    expect(patterns()).toEqual(['words b']);
  });
});
