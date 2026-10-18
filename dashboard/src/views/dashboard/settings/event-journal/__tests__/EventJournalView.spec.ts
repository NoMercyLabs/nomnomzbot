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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { eventJournalKey } from '@/api/eventJournal';
import { i18n } from '@/i18n';
import { dashboardSettingsRoutes } from '../../route';
import EventJournalView from '../EventJournalView.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  options: {
    params?: Record<string, unknown>;
    body?: unknown;
    bodySerializer?: (body: unknown) => unknown;
    parseAs?: string;
  };
};

const calls: Call[] = [];
const saved: { name: string; body: string }[] = [];
const refused = { path: '' };

const journalPage = {
  items: [
    {
      eventId: 'e2',
      streamPosition: 42,
      eventType: 'chat.message',
      source: 'twitch',
      occurredAt: '2026-10-01T10:00:00Z',
      recordedAt: '2026-10-01T10:00:01Z',
    },
    {
      eventId: 'e1',
      streamPosition: 41,
      eventType: 'channel.follow',
      source: 'kick',
      occurredAt: '2026-10-01T09:00:00Z',
      recordedAt: '2026-10-01T09:00:01Z',
    },
  ],
  page: 1,
  pageSize: 25,
  totalCount: 60,
  hasMore: true,
  headPosition: 42,
};

const answers: Record<string, unknown> = {
  '/export': '{"id":1}\n{"id":2}\n',
  '/import': {
    data: {
      totalLines: 10,
      imported: 7,
      skippedDuplicate: 2,
      upcast: 1,
    },
  },
  '/replay': { data: 12 },
  '/import-legacy': {
    data: {
      totalRead: 30,
      imported: 20,
      skippedUnmapped: 4,
      skippedDuplicate: 6,
      headBefore: 5,
      headAfter: 25,
      skippedByLegacyType: { 'legacy.odd': 4 },
    },
  },
  '/rebuild-projections': {
    data: [
      { projectionName: 'viewer-profiles', eventsApplied: 40 },
      { projectionName: 'analytics-dailies', eventsApplied: 9 },
    ],
  },
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options: Call['options']) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      return {
        data: { data: journalPage },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options: Call['options']) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (path.endsWith(refused.path) && refused.path !== '') {
        return {
          error: { detail: 'Owner only.' },
          response: { status: 403, ok: false },
        };
      }

      const key = Object.keys(answers).find((suffix) => path.endsWith(suffix)) as string;

      return {
        data: answers[key],
        response: { status: 200, ok: true },
      };
    },
  },
}));

vi.mock('@/lib/download', () => ({
  downloadText: (name: string, body: string) => {
    saved.push({ name, body });
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let queries: QueryClient;

beforeEach(() => {
  calls.length = 0;
  saved.length = 0;
  refused.path = '';
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
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(EventJournalView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = (selector: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(selector) as HTMLElement;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const callTo = (suffix: string): Call | undefined =>
  calls.find((call) => call.path.endsWith(suffix));

describe('Event journal page', () => {
  it('saves the journal as a .jsonl file named for the channel and shows how big it was', async () => {
    await open();
    await click(q('[data-export]'));

    expect(callTo('/export')?.options.params).toMatchObject({ path: { channelId: 'c1' } });
    expect(callTo('/export')?.options.parseAs).toBe('text');
    expect(saved).toHaveLength(1);
    expect(saved[0]!.name).toMatch(/^event-journal-c1-\d{4}-\d{2}-\d{2}\.jsonl$/);
    expect(saved[0]!.body).toBe('{"id":1}\n{"id":2}\n');
    expect(q('[data-export-result]').textContent).toContain('2');
  });

  it('uploads a chosen file as multipart form data and reports the counts', async () => {
    await open();

    const input = q('[data-import-file]') as HTMLInputElement;
    const file = new File(['{"id":1}\n'], 'journal.jsonl', { type: 'application/x-ndjson' });

    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await settle();

    const sent = callTo('/import');
    const form = sent?.options.bodySerializer?.(sent.options.body) as FormData;

    expect(form).toBeInstanceOf(FormData);
    expect((form.get('file') as File).name).toBe('journal.jsonl');

    const result = q('[data-import-result]').textContent as string;

    expect(result).toContain('7');
    expect(result).toContain('2');
  });

  it('replays imported events and says how many it applied', async () => {
    await open();
    await click(q('[data-replay]'));

    expect(callTo('/replay')?.options.params).toMatchObject({ path: { channelId: 'c1' } });
    expect(q('[data-replay-result]').textContent).toContain('12');
  });

  it('asks before it rebuilds the read models, and rebuilds only after the confirm', async () => {
    await open();
    await click(q('[data-rebuild]'));

    expect(callTo('/rebuild-projections')).toBeUndefined();
    expect(document.body.textContent).toContain('Viewer profiles');

    await click(q('[data-confirm-action]'));

    expect(callTo('/rebuild-projections')?.options.params).toMatchObject({
      path: { channelId: 'c1' },
    });

    const result = q('[data-rebuild-result]').textContent as string;

    expect(result).toContain('viewer-profiles');
    expect(result).toContain('40');
  });

  it('imports the old bot history and itemizes what it skipped', async () => {
    await open();
    await click(q('[data-import-legacy]'));

    expect(callTo('/import-legacy')?.options.params).toMatchObject({ path: { channelId: 'c1' } });

    const result = q('[data-legacy-result]').textContent as string;

    expect(result).toContain('20');
    expect(result).toContain('legacy.odd');
  });

  it('refreshes the channel read models after a replay, so other pages show the new facts', async () => {
    await open();
    queries.setQueryData(['channels', 'c1', 'commands'], []);
    queries.setQueryData(['channels', 'c2', 'commands'], []);

    await click(q('[data-replay]'));

    expect(queries.getQueryState(['channels', 'c1', 'commands'])?.isInvalidated).toBe(true);
    expect(queries.getQueryState(['channels', 'c2', 'commands'])?.isInvalidated).toBe(false);
  });

  it('shows the reason the bot gave when it refuses, and no result', async () => {
    refused.path = '/replay';
    await open();
    await click(q('[data-replay]'));

    expect(q('[role="alert"]').textContent).toContain('Owner only.');
    expect(document.body.querySelector('[data-replay-result]')).toBeNull();
  });
});

describe('Event journal list', () => {
  const journalCalls = (): Call[] => calls.filter((call) => call.method === 'GET');

  it('lists the events newest first with their position, type and source', async () => {
    window.localStorage.clear();
    await open();

    const rows = document.body.querySelectorAll('[data-journal-row]');

    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('42');
    expect(rows[0]!.textContent).toContain('chat.message');
    expect(rows[0]!.textContent).toContain('twitch');
    expect(rows[1]!.textContent).toContain('channel.follow');

    expect(journalCalls()[0]!.options).toMatchObject({
      params: { path: { channelId: 'c1' }, query: { Page: 1, Take: 25 } },
    });
  });

  it('sends the typed filters to the bot and returns to page 1', async () => {
    window.localStorage.clear();
    await open();
    await click(q('[data-next]'));

    expect(journalCalls().at(-1)!.options.params).toMatchObject({ query: { Page: 2 } });

    const type = q('[data-filter-type]') as HTMLInputElement;

    type.value = 'channel.follow';
    type.dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((done) => setTimeout(done, 400));
    await settle();

    expect(journalCalls().at(-1)!.options.params).toMatchObject({
      query: { Page: 1, eventType: 'channel.follow' },
    });
  });

  it('keeps a filter after the page is opened again', async () => {
    window.localStorage.clear();
    await open();

    const source = q('[data-filter-source]') as HTMLInputElement;

    source.value = 'kick';
    source.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    mounted?.unmount();
    await open();

    expect((q('[data-filter-source]') as HTMLInputElement).value).toBe('kick');
  });

  it('reads the list again when the channel journal changes', async () => {
    window.localStorage.clear();
    await open();

    const before = journalCalls().length;

    await queries.invalidateQueries({ queryKey: eventJournalKey('c1') });
    await settle();

    expect(journalCalls().length).toBeGreaterThan(before);
  });
});

describe('Event journal route', () => {
  it('carries the action key and loads the real page', async () => {
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-event-journal',
    );
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(record?.meta).toEqual({ navMode: 'settings', action: 'eventstore:export' });
    expect(loaded.default).toBe(EventJournalView);
  });
});
