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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter, createWebHistory, type Router, type RouterHistory } from 'vue-router';

import { position, pressBack } from '@/__tests__/backHistory';
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
let held: string[] = ['timers:read', 'chattriggers:read', 'chattriggers:write'];

function read(path: string): unknown {
  if (path.endsWith('/roles/effective/me'))
    return { heldActionKeys: held };

  return path.endsWith('/chat-triggers')
    ? rows
    : [];
}

const trigger = (id: string, overrides: Row = {}) => ({
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
}) as const satisfies Row;

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
      data: { data: read(path) },
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
let router: Router;

beforeEach(() => {
  calls.length = 0;
  rows = [trigger('a'), trigger('b', { isEnabled: false })];
  refusePatch = null;
  held = ['timers:read', 'chattriggers:read', 'chattriggers:write'];
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

async function open(
  history: RouterHistory = createMemoryHistory(),
  url: string = '/app/triggers-timers?view=triggers',
  before?: string,
): Promise<void> {
  router = createRouter({
    history,
    routes: [
      { path: '/app/triggers-timers', component: { template: '<div />' } },
      { path: '/home', component: { template: '<div />' } },
    ],
  });

  if (before) {
    await router.push(before);
  }

  await router.push(url);

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

  it('shows the reply or the pipeline picker, never both', async () => {
    await open();
    await click(rowOf('words a'));
    expect(document.body.querySelector('#trigger-response')).not.toBeNull();
    expect(document.body.querySelector('[data-reply-pipeline]')).toBeNull();

    await click(buttonByText('Pipeline'));

    expect(document.body.querySelector('#trigger-response')).toBeNull();
    expect(document.body.querySelector('[data-reply-pipeline]')).not.toBeNull();
  });

  it('opens a trigger that runs a pipeline in Full mode and clears the pipeline when Simple is saved', async () => {
    rows = [trigger('a', { pipelineId: 'p1', response: 'Fallback text' })];

    await open();
    await click(rowOf('words a'));
    expect(document.body.querySelector('#trigger-response')).toBeNull();

    await click(buttonByText('Simple reply'));

    expect((document.body.querySelector('#trigger-response') as HTMLTextAreaElement).value).toBe(
      'Fallback text',
    );

    await click(buttonByText('Save changes'));

    expect(calls.filter((call) => call.method === 'PATCH')).toEqual([
      {
        method: 'PATCH',
        id: 'a',
        body: { pipelineId: '00000000-0000-0000-0000-000000000000' },
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
      replyDelivery: { target: 'chat', html: false },
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

// Stoney 2026-11-03: "a close is a history pop". The real router on the real history.
describe('Chat triggers tab history', () => {
  const tab = '/app/triggers-timers?view=triggers';
  const editorOpen = (): boolean => document.body.querySelector('[data-trigger-editor]') !== null;

  beforeEach(() => {
    window.history.replaceState(null, '', '/');
  });

  it('opens a trigger with one step, and Back closes it before it leaves the page', async () => {
    await open(createWebHistory(), tab, '/home');
    const start = position();

    await click(rowOf('words b'));
    expect(position()).toBe(start + 1);
    expect(router.currentRoute.value.fullPath).toBe(`${tab}&trigger=b`);
    expect(editorOpen()).toBe(true);

    await pressBack();
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe(tab);
    expect(editorOpen()).toBe(false);

    await pressBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });

  it('closes a deleted pasted-link trigger by replacing, so Back leaves the page', async () => {
    await open(createWebHistory(), `${tab}&trigger=a`, '/home');
    const start = position();

    await click(buttonByText('Delete'));
    await click(buttonByText('Delete trigger'));
    expect(position()).toBe(start);
    expect(router.currentRoute.value.fullPath).toBe(tab);

    await pressBack();
    expect(router.currentRoute.value.fullPath).toBe('/home');
  });
});

describe('Chat trigger reply delivery', () => {
  const chosen = (): string | undefined =>
    document.body
      .querySelector('[data-trigger-editor] [data-reply-delivery] [data-state="on"]')
      ?.textContent?.trim();

  it('shows the reply delivery field next to the reply of a trigger', async () => {
    await open();
    await click(rowOf('words a'));

    expect(document.body.querySelector('[data-trigger-editor] [data-reply-delivery]')).not.toBeNull();
  });

  it('shows Twitch chat for a trigger that has no stored delivery, and saves nothing for it', async () => {
    await open();
    await click(rowOf('words a'));

    expect(chosen()).toBe('Twitch chat');
    expect(buttonByText('Save changes').disabled).toBe(true);
    expect(calls.filter((call) => call.method === 'PATCH')).toHaveLength(0);
  });

  it('shows the stored target of a trigger when it opens', async () => {
    rows = [trigger('a', { replyDelivery: { target: 'both', html: false } })];

    await open();
    await click(rowOf('words a'));

    expect(chosen()).toBe('Both');
  });

  it('sends the new delivery when the target changes, and no other field', async () => {
    await open();
    await click(rowOf('words a'));

    await click(
      [...document.body.querySelectorAll<HTMLButtonElement>('[data-reply-delivery] button')].find(
        (b) => b.textContent?.trim() === 'Chat overlay',
      )!,
    );

    await click(buttonByText('Save changes'));

    expect(calls.filter((call) => call.method === 'PATCH')[0]!.body).toEqual({
      replyDelivery: { target: 'overlay', html: false },
    });
  });

  const dialogTarget = (label: string): HTMLButtonElement =>
    [...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] [data-reply-delivery] button')].find(
      (b) => b.textContent?.trim() === label,
    ) as HTMLButtonElement;

  it('shows the reply delivery field in the new trigger dialog and sends chat when untouched', async () => {
    await open();
    await click(buttonByText('New trigger'));

    expect(document.body.querySelector('[role="dialog"] [data-reply-delivery]')).not.toBeNull();

    await type(field('Words to watch for'), 'hello bot');
    await type(field('Reply'), 'Hi!');
    await click(buttonByText('Create trigger'));

    expect(calls.filter((call) => call.method === 'POST')[0]!.body).toEqual({
      pattern: 'hello bot',
      response: 'Hi!',
      replyDelivery: { target: 'chat', html: false },
    });
  });

  it('sends the chosen delivery from the new trigger dialog', async () => {
    await open();
    await click(buttonByText('New trigger'));
    await type(field('Words to watch for'), 'hello bot');
    await type(field('Reply'), 'Hi!');
    await click(dialogTarget('Chat overlay'));
    await click(buttonByText('Create trigger'));

    expect(calls.filter((call) => call.method === 'POST')[0]!.body).toMatchObject({
      replyDelivery: { target: 'overlay', html: false },
    });
  });

  it('keeps the stored delivery when another field changes', async () => {
    rows = [trigger('a', { replyDelivery: { target: 'overlay', html: true } })];

    await open();
    await click(rowOf('words a'));
    await type(field('Seconds between replies'), '90');
    await click(buttonByText('Save changes'));

    expect(calls.filter((call) => call.method === 'PATCH')[0]!.body).toEqual({ cooldownSeconds: 90 });
  });
});

describe('Chat triggers permission gating', () => {
  it('shows a reader the trigger list, a New trigger that is in place but off, no editor on a picked row', async () => {
    held = ['timers:read', 'chattriggers:read'];
    await open();
    await click(rowOf('words a'));

    expect(patterns()).toEqual(['words a', 'words b']);
    expect(buttonByText('New trigger').disabled).toBe(true);
    expect(document.body.querySelector('[data-trigger-editor]')).toBeNull();
  });

  it('shows a person with chattriggers:write the New trigger button and the editor', async () => {
    await open();
    await click(rowOf('words a'));

    expect(buttonByText('New trigger')).toBeDefined();
    expect(document.body.querySelector('[data-trigger-editor]')).not.toBeNull();
  });

  it('hides the first-trigger button in the empty list from a reader', async () => {
    held = ['timers:read', 'chattriggers:read'];
    rows = [];
    await open();

    expect(buttonByText('Make the first trigger')).toBeUndefined();
  });
});
