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

import QuotesView from '../QuotesView.vue';
import en from '../i18n/en.json';

type QuoteRow = {
  id: string;
  number: number;
  text: string;
  quotedDisplayName: string | null;
  contextGame: string | null;
  quotedAt: string | null;
  createdAt: string;
};

const quote = (number: number, text: string, saidBy: string | null): QuoteRow => ({
  id: `q${number}`,
  number,
  text,
  quotedDisplayName: saidBy,
  contextGame: 'Elden Ring',
  quotedAt: '2026-10-01T12:00:00Z',
  createdAt: '2026-10-01T12:00:00Z',
});

let quotes: QuoteRow[] = [];
const calls: string[] = [];
const bodies: Record<string, unknown>[] = [];
const searches: (string | undefined)[] = [];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, init?: { params?: { query?: { Search?: string } } }) => {
      if (path.endsWith('/random'))
        return { data: { data: quotes[0] }, response: { status: 200, ok: true } };

      searches.push(init?.params?.query?.Search);

      const search = init?.params?.query?.Search?.toLowerCase();
      const matching = quotes.filter((row) => !search || row.text.toLowerCase().includes(search));

      return {
        data: {
          data: matching,
          total: matching.length,
          hasMore: false,
        },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, init: { body: Record<string, unknown> }) => {
      calls.push(`POST ${path}`);
      bodies.push(init.body);

      return { data: {}, response: { status: 200, ok: true } };
    },
    PUT: async (path: string, init: { body: Record<string, unknown> }) => {
      calls.push(`PUT ${path}`);
      bodies.push(init.body);

      return { data: {}, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, init: { params: { path: { number: number } } }) => {
      calls.push(`DELETE ${path} ${init.params.path.number}`);
      quotes = quotes.filter((row) => row.number !== init.params.path.number);

      return { data: {}, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: ref({ id: 'u-me' }) }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  bodies.length = 0;
  searches.length = 0;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 400));
  await flushPromises();
}

async function open(): Promise<QueryClient> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(QuotesView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();

  return queryClient;
}

async function fill(selector: string, value: string): Promise<void> {
  const input = document.querySelector<HTMLInputElement>(selector);

  if (!input)
    throw new Error(`missing ${selector}`);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

function submitDialog(): void {
  document
    .querySelector('form')
    ?.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
}

const rows = (): string[] =>
  Array.from(document.querySelectorAll('[data-quote-row]')).map((row) => row.textContent ?? '');

describe('QuotesView', () => {
  it('lists each quote with its number, text, speaker and game', async () => {
    quotes = [quote(2, 'Never again', 'Kitte'), quote(1, 'Hello chat', null)];
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('#2');
    expect(rows()[0]).toContain('Never again');
    expect(rows()[0]).toContain('Kitte');
    expect(rows()[0]).toContain('Elden Ring');
    expect(rows()[1]).not.toContain('Kitte');
  });

  it('searches on the server with the typed text', async () => {
    quotes = [quote(2, 'Never again', 'Kitte'), quote(1, 'Hello chat', null)];
    await open();

    await fill('[data-quote-search] , input[type="search"]', 'hello');
    await settle();

    expect(searches).toContain('hello');
    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('Hello chat');
  });

  it('adds a quote with the signed-in user as author and no date when the field is empty', async () => {
    quotes = [];
    await open();

    document.querySelector<HTMLElement>('[data-quote-new]')?.click();
    await settle();
    await fill('[data-quote-text]', 'A new line');
    await fill('[data-quote-said-by]', ' Bamo ');
    submitDialog();
    await settle();

    expect(calls).toEqual(['POST /api/v{version}/quotes']);

    expect(bodies[0]).toEqual({
      text: 'A new line',
      quotedDisplayName: 'Bamo',
      contextGame: null,
      quotedAt: null,
      createdByUserId: 'u-me',
    });
  });

  it('edits a quote by its number and sends the date it was said', async () => {
    quotes = [quote(7, 'Old line', 'Kitte')];
    await open();

    document.querySelector<HTMLElement>('[data-quote-edit]')?.click();
    await settle();

    expect(document.querySelector<HTMLTextAreaElement>('[data-quote-text]')?.value).toBe(
      'Old line',
    );

    await fill('[data-quote-text]', 'New line');
    submitDialog();
    await settle();

    expect(calls).toEqual(['PUT /api/v{version}/quotes/{number}']);

    expect(bodies[0]).toMatchObject({
      text: 'New line',
      quotedDisplayName: 'Kitte',
      contextGame: 'Elden Ring',
    });

    expect(bodies[0]).not.toHaveProperty('createdByUserId');
    expect(typeof bodies[0]?.quotedAt).toBe('string');
  });

  it('refuses an empty quote and says why, without saving', async () => {
    quotes = [];
    await open();

    document.querySelector<HTMLElement>('[data-quote-new]')?.click();
    await settle();
    submitDialog();
    await settle();

    expect(calls).toEqual([]);
    expect(document.body.textContent).toContain(en.dashboard.quotes.dialog.problem.textRequired);
  });

  it('deletes a quote after the confirm and the row goes away', async () => {
    quotes = [quote(3, 'Gone soon', 'Kitte')];
    await open();

    document.querySelector<HTMLElement>('[data-quote-delete]')?.click();
    await settle();

    expect(calls).toEqual([]);

    document.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await settle();

    expect(calls).toEqual(['DELETE /api/v{version}/quotes/{number} 3']);
    expect(rows()).toHaveLength(0);
  });

  it('shows a random quote from the server', async () => {
    quotes = [quote(5, 'Pick me', 'Kitte')];
    await open();

    document.querySelector<HTMLElement>('[data-quote-random]')?.click();
    await settle();

    expect(document.querySelector('[data-random-quote]')?.textContent).toContain('Pick me');
  });
});
