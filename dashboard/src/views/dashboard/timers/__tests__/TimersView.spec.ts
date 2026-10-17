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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { timersKey } from '@/api/liveOps';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import TimersView from '../TimersView.vue';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  id: unknown;
  body: Record<string, unknown> | undefined;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let rows: Row[] = [];
let refusePut: string | null = null;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const timer = (id: string, overrides: Row = {}): Row => ({
  id,
  name: `Timer ${id}`,
  messages: ['Follow the channel'],
  messageCount: 1,
  intervalMinutes: 15,
  minChatActivity: 3,
  isEnabled: true,
  fireOnce: false,
  speakWithTts: false,
  pipelineId: null,
  lastFiredAt: null,
  nextMessageIndex: 0,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  ...overrides,
});

function read(path: string, options?: Options): Answer {
  const id = options?.params?.path?.id;

  if (typeof id === 'string')
    return ok(rows.find((row) => row.id === id));

  if (path.endsWith('/timers'))
    return ok(rows.map((row) => ({ ...row, messageCount: (row.messages as string[]).length })));

  return ok(null);
}

function write(method: string, path: string, options?: Options): Answer {
  const id = options?.params?.path?.id;

  calls.push({
    method,
    path,
    id,
    body: options?.body,
  });

  if (method === 'PUT' && refusePut)
    return { error: { message: refusePut }, response: { status: 400, ok: false } };

  if (method === 'PUT') {
    rows = rows.map((row) =>
      row.id === id
        ? { ...row, ...options?.body }
        : row);
  }

  if (method === 'DELETE') {
    rows = rows.filter((row) => row.id !== id);
  }

  if (method === 'POST' && path.endsWith('/toggle')) {
    rows = rows.map((row) =>
      row.id === id
        ? { ...row, isEnabled: !row.isEnabled }
        : row);
  }

  if (method === 'POST' && path.endsWith('/timers')) {
    rows.push(timer('new', options?.body));
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => read(path, options),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let router: Router;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;

  rows = [
    timer('t1', { name: 'Follow reminder' }),
    timer('t2', {
      name: 'Discord invite',
      isEnabled: false,
      messages: ['Join us', 'Link in bio'],
    }),
  ];

  refusePut = null;
  pinia = createPinia();
  setActivePinia(pinia);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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

async function open(url: string = '/app/triggers-timers'): Promise<void> {
  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/triggers-timers',
        name: 'dashboard-triggers-timers',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(url);

  mounted = mount(TimersView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const rowNames = (): (string | undefined)[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-timer-row]')].map(
    (row) => row.dataset.timerName,
  );
const rowOf = (name: string): HTMLElement =>
  document.body.querySelector(`[data-timer-name="${name}"]`) as HTMLElement;
const editor = (): HTMLElement | null => document.body.querySelector('[data-timer-editor]');
const buttonByText = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
const field = (label: string): HTMLInputElement =>
  document.body.querySelector(`[aria-label="${label}"]`) as HTMLInputElement;
const switchOf = (title: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"], label')]
    .find((row) => row.textContent?.includes(title))
    ?.querySelector('button[role="switch"]') as HTMLButtonElement;
const writes = (method: string): Call[] => calls.filter((call) => call.method === method);

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

describe('Triggers and timers page', () => {
  it('lists every timer of the channel', async () => {
    await open();

    expect(rowNames()).toEqual(['Follow reminder', 'Discord invite']);
  });

  it('opens the timer in the editor with its saved values', async () => {
    await open();
    await click(rowOf('Discord invite'));

    expect(editor()).not.toBeNull();
    expect(field('Name').value).toBe('Discord invite');
    expect(field('Minutes between posts').value).toBe('15');
    expect(field('Messages').value).toBe('Join us\nLink in bio');
  });

  it('saves only the changed fields, and a messages list one message per line', async () => {
    await open();
    await click(rowOf('Follow reminder'));
    await type(field('Minutes between posts'), '30');
    await type(field('Messages'), 'One\nTwo');
    await click(buttonByText('Save changes'));

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.id).toBe('t1');
    expect(writes('PUT')[0]!.body).toEqual({ intervalMinutes: 30, messages: ['One', 'Two'] });
  });

  it('keeps the draft and shows the reason when the bot refuses the save', async () => {
    refusePut = 'Interval too short';
    await open();
    await click(rowOf('Follow reminder'));
    await type(field('Minutes between posts'), '1');
    await click(buttonByText('Save changes'));

    expect(editor()!.querySelector('[role="alert"]')?.textContent).toContain('Interval too short');
    expect(field('Minutes between posts').value).toBe('1');
  });

  it('turns a timer on from the editor switch and the list shows it', async () => {
    await open();
    await click(rowOf('Discord invite'));
    await click(switchOf('Enabled'));

    expect(writes('POST').map((call) => [call.path.endsWith('/toggle'), call.id])).toEqual([
      [true, 't2'],
    ]);

    expect(rowOf('Discord invite').textContent).not.toContain('Off');
  });

  it('creates a timer from the dialog and selects it', async () => {
    await open();
    await click(buttonByText('New timer'));
    await type(field('Name'), 'Hydrate');
    await type(field('Messages'), 'Drink water\nStretch');
    await click(buttonByText('Create timer'));

    expect(writes('POST')).toHaveLength(1);

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'Hydrate',
      messages: ['Drink water', 'Stretch'],
      intervalMinutes: 15,
    });

    expect(rowNames()).toContain('Hydrate');
  });

  it('deletes a timer only after the confirm and drops it from the list', async () => {
    await open();
    await click(rowOf('Follow reminder'));
    await click(buttonByText('Delete'));

    expect(writes('DELETE')).toHaveLength(0);

    await click(buttonByText('Delete timer'));

    expect(writes('DELETE').map((call) => call.id)).toEqual(['t1']);
    expect(rowNames()).toEqual(['Discord invite']);
  });

  it('shows a timer another dashboard added once the live signal marks the list stale', async () => {
    await open();

    rows.push(timer('t3', { name: 'Raid call' }));
    await queries.invalidateQueries({ queryKey: timersKey('c1') });
    await settle();

    expect(rowNames()).toContain('Raid call');
  });
});
