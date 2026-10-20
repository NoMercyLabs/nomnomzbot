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
import VoiceTriggersView from '../VoiceTriggersView.vue';

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
let held: string[] = ['voicetriggers:read', 'voicetriggers:write'];

const voice = (id: string, overrides: Row = {}) => ({
  id,
  word: `word ${id}`,
  isEnabled: true,
  startingCount: 0,
  currentCount: 7,
  cooldownSeconds: 5,
  stickerAssetId: null,
  stickerImageUrl: null,
  lastFiredAt: null,
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
    rows.push(voice('new', options?.body));
  }

  return { response: { status: 200, ok: true } };
}

function read(path: string): unknown {
  if (path.endsWith('/roles/effective/me'))
    return { heldActionKeys: held };

  if (path.endsWith('/voice-triggers'))
    return rows;

  if (path.endsWith('/listener-link'))
    return 'https://example.test/voice';

  return [];
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

beforeEach(() => {
  calls.length = 0;
  rows = [voice('a'), voice('b', { isEnabled: false })];
  refusePatch = null;
  held = ['voicetriggers:read', 'voicetriggers:write'];
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
    routes: [{ path: '/app/voice-triggers', component: { template: '<div />' } }],
  });

  await router.push('/app/voice-triggers');

  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(VoiceTriggersView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const words = (): (string | undefined)[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-voice-row]')].map(
    (row) => row.dataset.voiceWord,
  );
const rowOf = (word: string): HTMLElement =>
  document.body.querySelector(`[data-voice-word="${word}"]`) as HTMLElement;
const buttonByText = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
const field = (label: string): HTMLInputElement =>
  document.body.querySelector(`[aria-label="${label}"]`) as HTMLInputElement;

async function type(el: HTMLInputElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const patches = (): Call[] => calls.filter((call) => call.method === 'PATCH');

describe('Voice triggers tab', () => {
  it('lists the voice triggers of the channel with their counts', async () => {
    await open();

    expect(words()).toEqual(['word a', 'word b']);
    expect(rowOf('word a').textContent).toContain('Heard 7 times');
  });

  it('sends only the changed fields in a PATCH', async () => {
    await open();
    await click(rowOf('word a'));
    await type(field('Seconds between counts'), '90');
    await click(buttonByText('Save changes'));

    expect(patches()).toEqual([
      {
        method: 'PATCH',
        id: 'a',
        body: { cooldownSeconds: 90 },
      },
    ]);
  });

  it('saves the Enabled switch at once as the one changed field', async () => {
    await open();
    await click(rowOf('word a'));
    await click(document.body.querySelector('[data-voice-editor] [role="switch"]') as HTMLElement);

    expect(patches()).toEqual([
      {
        method: 'PATCH',
        id: 'a',
        body: { isEnabled: false },
      },
    ]);
  });

  it('keeps the draft and shows the reason when the bot refuses the save', async () => {
    refusePatch = 'Word already used';
    await open();
    await click(rowOf('word a'));
    await type(field('Word to listen for'), 'changed');
    await click(buttonByText('Save changes'));

    expect(document.body.querySelector('[data-voice-editor] [role="alert"]')?.textContent).toContain(
      'Word already used',
    );

    expect(field('Word to listen for').value).toBe('changed');
  });

  it('creates a voice trigger with its starting count and refetches the list', async () => {
    await open();
    await click(buttonByText('New voice trigger'));
    await type(field('Word to listen for'), 'nomnom');
    await type(field('Start counting at'), '12');
    await click(buttonByText('Create voice trigger'));

    expect(calls.filter((call) => call.method === 'POST')[0]!.body).toEqual({
      word: 'nomnom',
      startingCount: 12,
      cooldownSeconds: 5,
    });

    expect(words()).toContain('nomnom');
  });

  it('refuses an empty word without calling the bot', async () => {
    await open();
    await click(buttonByText('New voice trigger'));
    await click(buttonByText('Create voice trigger'));

    expect(calls).toHaveLength(0);
    expect(document.body.textContent).toContain('Give the trigger a word to listen for.');
  });

  it('deletes a voice trigger only after the confirm and refetches the list', async () => {
    await open();
    await click(rowOf('word a'));
    await click(buttonByText('Delete'));

    expect(calls.filter((call) => call.method === 'DELETE')).toHaveLength(0);

    await click(buttonByText('Delete voice trigger'));

    expect(calls.filter((call) => call.method === 'DELETE').map((call) => call.id)).toEqual(['a']);
    expect(words()).toEqual(['word b']);
  });
});

describe('Voice triggers permission gating', () => {
  it('shows a reader the list only: no New voice trigger, no editor on a picked row', async () => {
    held = ['voicetriggers:read'];
    await open();
    await click(rowOf('word a'));

    expect(words()).toEqual(['word a', 'word b']);
    expect(buttonByText('New voice trigger')).toBeUndefined();
    expect(document.body.querySelector('[data-voice-editor]')).toBeNull();
  });

  it('shows a person with voicetriggers:write the New voice trigger button and the editor', async () => {
    await open();
    await click(rowOf('word a'));

    expect(buttonByText('New voice trigger')).toBeDefined();
    expect(document.body.querySelector('[data-voice-editor]')).not.toBeNull();
  });

  it('hides the first-trigger button in the empty list from a reader', async () => {
    held = ['voicetriggers:read'];
    rows = [];
    await open();

    expect(buttonByText('Make the first voice trigger')).toBeUndefined();
  });
});

describe('VoiceTriggersView header listener', () => {
  it('opens the listener link in a popup window from the header Popout button', async () => {
    const popup = vi.spyOn(window, 'open').mockReturnValue(null);

    await open();

    const header = document.body.querySelector('[data-voice-listener-header]') as HTMLElement;
    const button = header.querySelector<HTMLButtonElement>('[data-voice-listener-popout]') as HTMLButtonElement;

    button.click();

    expect(popup).toHaveBeenCalledWith('https://example.test/voice', 'nnz-voice-listener', 'popup,width=520,height=560');
    popup.mockRestore();
  });

  it('shows the latest heard match beside the Popout button', async () => {
    rows = [voice('a', { lastFiredAt: '2020-01-01T00:00:00Z' }), voice('b', { lastFiredAt: new Date().toISOString() })];
    await open();

    const status = document.body.querySelector('[data-voice-listener-header] [data-voice-listener-status]') as HTMLElement;

    expect(status.textContent?.trim()).toBe('Last match: just now');
    // A past match is history, not proof a listener tab is open now, so the dot stays neutral.
    expect(status.dataset.tone).toBe('off');
  });

  it('says no match yet when no trigger was heard', async () => {
    await open();

    const status = document.body.querySelector('[data-voice-listener-status]') as HTMLElement;

    expect(status.textContent?.trim()).toBe('No match heard yet');
    expect(status.dataset.tone).toBe('off');
  });
});
