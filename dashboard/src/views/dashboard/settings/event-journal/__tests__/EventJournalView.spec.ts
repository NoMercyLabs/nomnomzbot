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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { eventJournalKey, type JournalEntry } from '@/api/eventJournal';
import { i18n } from '@/i18n';
import { keysOf } from '@/lib/array';
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
      eventType: 'FollowEvent',
      source: 'eventsub',
      occurredAt: '2026-10-01T10:00:00Z',
      recordedAt: '2026-10-01T10:00:01Z',
      actorUserId: 'u-ada',
      actorExternalUserId: '9001',
      actorProvider: 'twitch',
    },
    {
      eventId: 'e1',
      streamPosition: 41,
      eventType: 'ObsStreamingStateChangedEvent',
      source: 'domain',
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

// A test that needs other rows sets them here; null keeps the two rows above.
const listed = { items: null as JournalEntry[] | null, total: null as number | null };

const bot = (eventId: string, eventType: string, occurredAt: string): JournalEntry => ({
  eventId,
  streamPosition: 1,
  eventType,
  source: 'domain',
  occurredAt,
  recordedAt: occurredAt,
});

const held = vi.hoisted(() => ({ keys: null as Set<string> | null }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string) => held.keys === null || held.keys.has(key),
  }),
}));

const answers = {
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
} as const satisfies Record<string, unknown>;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options: Call['options']) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      if (path.endsWith('/profile')) {
        return {
          data: { data: { identity: {
            displayName: 'Ada',
            twitchUserId: '9001',
            chatColor: null,
          } } },
          response: { status: 200, ok: true },
        };
      }

      return {
        data: { data: {
          ...journalPage,
          items: listed.items ?? journalPage.items,
          totalCount: listed.total ?? journalPage.totalCount,
        } },
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

      if (path.endsWith('/export') && slow.gate !== null) {
        await slow.gate;
      }

      const key = keysOf(answers).find((suffix) => path.endsWith(suffix))!;

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

// A slow export (the server took 102.7 s for 1.4 million events) is held open until the test lets it go.
const slow = { gate: null as Promise<void> | null };

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let queries: QueryClient;
let router: Router;

beforeEach(() => {
  calls.length = 0;
  saved.length = 0;
  refused.path = '';
  held.keys = null;
  slow.gate = null;
  listed.items = null;
  listed.total = null;
  i18n.global.mergeLocaleMessage('en', en);
  setActivePinia(createPinia());
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

const pagePath = '/app/settings/event-journal';

async function open(url: string = pagePath): Promise<void> {
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: pagePath, component: { template: '<div />' } }],
  });

  await router.push(url);

  mounted = mount(EventJournalView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        router,
        [VueQueryPlugin, { queryClient: queries }],
      ],
    },
  });

  await settle();
}

// Export, import, replay and rebuild live on the second tab of the page.
const openTools = (): Promise<void> => open(`${pagePath}?view=tools`);

const q = (selector: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(selector) as HTMLElement;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const callTo = (suffix: string): Call | undefined =>
  calls.find((call) => call.path.endsWith(suffix));

describe('Event journal backup and rebuild tab', () => {
  it('keeps export, import, replay and rebuild off the events tab and shows them on the tools tab', async () => {
    window.localStorage.clear();
    await open();

    const tools = ['[data-export]', '[data-import]', '[data-replay]', '[data-rebuild]', '[data-import-legacy]'];

    expect(tools.filter((selector) => document.body.querySelector(selector) !== null)).toEqual([]);
    expect(document.body.querySelector('[data-panel="journal-list"]')).not.toBeNull();

    mounted?.unmount();
    await openTools();

    expect(tools.filter((selector) => document.body.querySelector(selector) === null)).toEqual([]);
    expect(document.body.querySelector('[data-panel="journal-list"]')).toBeNull();
    expect(router.currentRoute.value.query.view).toBe('tools');
  });

  it('says the export is working while the server prepares the file, and saves it when it arrives', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });

    let release = (): void => undefined;

    slow.gate = new Promise<void>((done) => {
      release = done;
    });

    try {
      await openTools();
      await click(q('[data-export]'));

      expect((q('[data-export]') as HTMLButtonElement).disabled).toBe(true);
      expect(q('[data-export-progress]').textContent).toContain('Preparing the file');

      vi.advanceTimersByTime(65_000);
      await flushPromises();

      expect(q('[data-export-progress]').textContent).toContain('1:05');
      expect(saved).toHaveLength(0);

      release();
      await settle();

      expect(document.body.querySelector('[data-export-progress]')).toBeNull();
      expect(saved).toHaveLength(1);
      expect(q('[data-export-result]').textContent).toContain('2');
    } finally {
      vi.useRealTimers();
    }
  });

  it('saves the journal as a .jsonl file named for the channel and shows how big it was', async () => {
    await openTools();
    await click(q('[data-export]'));

    expect(callTo('/export')?.options.params).toMatchObject({ path: { channelId: 'c1' } });
    expect(callTo('/export')?.options.parseAs).toBe('text');
    expect(saved).toHaveLength(1);
    expect(saved[0]!.name).toMatch(/^event-journal-c1-\d{4}-\d{2}-\d{2}\.jsonl$/);
    expect(saved[0]!.body).toBe('{"id":1}\n{"id":2}\n');
    expect(q('[data-export-result]').textContent).toContain('2');
  });

  it('uploads a chosen file as multipart form data and reports the counts', async () => {
    await openTools();

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
    await openTools();
    await click(q('[data-replay]'));

    expect(callTo('/replay')?.options.params).toMatchObject({ path: { channelId: 'c1' } });
    expect(q('[data-replay-result]').textContent).toContain('12');
  });

  it('asks before it rebuilds the read models, and rebuilds only after the confirm', async () => {
    await openTools();
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
    await openTools();
    await click(q('[data-import-legacy]'));

    expect(callTo('/import-legacy')?.options.params).toMatchObject({ path: { channelId: 'c1' } });

    const result = q('[data-legacy-result]').textContent as string;

    expect(result).toContain('20');
    expect(result).toContain('legacy.odd');
  });

  it('refreshes the channel read models after a replay, so other pages show the new facts', async () => {
    await openTools();
    queries.setQueryData(['channels', 'c1', 'commands'], []);
    queries.setQueryData(['channels', 'c2', 'commands'], []);

    await click(q('[data-replay]'));

    expect(queries.getQueryState(['channels', 'c1', 'commands'])?.isInvalidated).toBe(true);
    expect(queries.getQueryState(['channels', 'c2', 'commands'])?.isInvalidated).toBe(false);
  });

  it('shows the reason the bot gave when it refuses, and no result', async () => {
    refused.path = '/replay';
    await openTools();
    await click(q('[data-replay]'));

    expect(q('[role="alert"]').textContent).toContain('Owner only.');
    expect(document.body.querySelector('[data-replay-result]')).toBeNull();
  });
});

describe('Event journal list', () => {
  const journalCalls = (): Call[] =>
    calls.filter((call) => call.path.endsWith('/journal'));

  it('says what happened in the channel in plain words, with the person in the sentence and when', async () => {
    window.localStorage.clear();
    await open();

    const rows = document.body.querySelectorAll('[data-journal-row]');

    // OBS state is the bot's own activity, so the default list shows only the follow.
    expect(rows).toHaveLength(1);
    expect(rows[0]!.querySelector('[data-what]')!.textContent).toBe('New follower');
    expect(rows[0]!.querySelector('[data-user="9001"]')!.textContent).toContain('Ada');
    expect(rows[0]!.querySelector('time')).not.toBeNull();
    expect(rows[0]!.textContent).not.toContain('42');
    expect(rows[0]!.textContent).not.toContain('FollowEvent');
    expect(document.body.querySelector('thead')!.textContent).toBe('What happenedWhen');

    expect(calls.filter((call) => call.path.endsWith('/profile'))).toHaveLength(1);

    expect(journalCalls()[0]!.options).toMatchObject({
      params: { path: { channelId: 'c1' }, query: { Page: 1, Take: 100 } },
    });
  });

  it('filters by a kind of event and a source picked from a list, and returns to page 1', async () => {
    window.localStorage.clear();
    window.localStorage.setItem('nnz.event-journal.eventType', 'FollowEvent');
    window.localStorage.setItem('nnz.event-journal.source', 'eventsub');
    listed.total = 500;
    await open();

    expect(journalCalls().at(-1)!.options.params).toMatchObject({
      query: {
        Page: 1,
        eventType: 'FollowEvent',
        source: 'eventsub',
      },
    });

    expect(q('[data-filter-type]').textContent).toContain('New follower');
    expect(q('[data-filter-source]').textContent).toContain('Twitch');
    expect(document.body.querySelector('input[data-filter-type]')).toBeNull();

    await click(q('[data-next]'));

    expect(journalCalls().at(-1)!.options.params).toMatchObject({ query: { Page: 2 } });

    await click(q('[data-clear-filters]'));

    const last = journalCalls().at(-1)!.options.params as { query: Record<string, unknown> };

    expect(last.query).toEqual({ Page: 1, Take: 100 });
    expect(window.localStorage.getItem('nnz.event-journal.eventType')).toBe('');
  });

  it('names no person where none was saved: no empty who column, no dash and no note', async () => {
    window.localStorage.clear();

    listed.items = [
      bot('b2', 'FollowEvent', '2026-10-01T09:10:00Z'),
      bot('b1', 'RaidEvent', '2026-10-01T09:00:00Z'),
    ];

    await open();

    expect(document.body.querySelector('thead')!.textContent).not.toContain('Who');
    expect(document.body.querySelectorAll('[data-who]')).toHaveLength(0);
    expect(document.body.textContent).not.toContain('—');
    expect(document.body.querySelector('[data-who-note]')).toBeNull();
  });

  it('names an API key or the bot as the actor of a change that has no person, and names none on an old row', async () => {
    window.localStorage.clear();

    listed.items = [
      { ...bot('k3', 'FollowEvent', '2026-10-01T09:20:00Z'), actorKind: 'api_key' },
      { ...bot('k2', 'RaidEvent', '2026-10-01T09:10:00Z'), actorKind: 'system' },
      bot('k1', 'SubscribeEvent', '2026-10-01T09:00:00Z'),
    ];

    await open();

    const badges = [...document.body.querySelectorAll('[data-actor-kind]')].map(
      (badge) => badge.textContent?.trim(),
    );

    expect(badges).toEqual(['API key', 'Bot']);
  });

  it('shows only what happened in the channel by default, says how much bot activity it hid, and shows it with the switch', async () => {
    window.localStorage.clear();

    listed.items = [
      bot('b5', 'channel.chat.message', '2026-10-01T09:50:00Z'),
      bot('b4', 'ChatMessageReceivedEvent', '2026-10-01T09:40:00Z'),
      bot('b3', 'LedgerEntryRecordedEvent', '2026-10-01T09:30:00Z'),
      bot('b2', 'AdScheduleUpdatedEvent', '2026-10-01T09:20:00Z'),
      bot('b1', 'FollowEvent', '2026-10-01T09:10:00Z'),
      bot('w3', 'WidgetConnectedEvent', '2026-10-01T09:05:00Z'),
      bot('w2', 'StreamViewerCountSampledEvent', '2026-10-01T09:04:00Z'),
      bot('w1', 'ObsDashboardStateChangedEvent', '2026-10-01T09:03:00Z'),
    ];

    await open();

    const shown = (): (string | null)[] =>
      [...document.body.querySelectorAll('[data-what]')].map((cell) => cell.textContent);

    expect(shown()).toEqual(['Chat message', 'New follower']);
    expect(q('[data-hidden-count]').textContent).toContain('6');
    expect(q('[data-show-housekeeping]').closest('div')!.textContent).toContain('Show bot activity');

    await click(q('[data-show-housekeeping]'));

    expect(shown()).toHaveLength(8);
    expect(journalCalls().at(-1)!.options.params).toMatchObject({ query: { Take: 25 } });
  });

  it('never says an event happens in the future when the server clock runs ahead', async () => {
    window.localStorage.clear();
    const ahead = new Date(Date.now() + 2_000);

    listed.items = [bot('b1', 'FollowEvent', ahead.toISOString())];
    await open();

    expect(q('[data-journal-row] time').textContent).not.toMatch(/^in /);
  });

  it('folds the same event in a row into one line that says how many times it happened', async () => {
    window.localStorage.clear();

    listed.items = [
      bot('b4', 'CommunityStandingChangedEvent', '2026-10-01T09:30:00Z'),
      bot('b3', 'CommunityStandingChangedEvent', '2026-10-01T09:20:00Z'),
      bot('b2', 'CommunityStandingChangedEvent', '2026-10-01T09:10:00Z'),
      bot('b1', 'IntegrationTokenRefreshedEvent', '2026-10-01T09:00:00Z'),
    ];

    window.localStorage.setItem('nnz.event-journal.housekeeping', 'true');
    await open();

    const rows = document.body.querySelectorAll('[data-journal-row]');

    expect(rows).toHaveLength(2);
    expect(rows[0]!.querySelector('[data-what]')!.textContent).toContain('Community standing changed');
    expect(rows[0]!.querySelector('[data-repeat]')!.textContent).toBe('3 times');
    expect(rows[1]!.querySelector('[data-repeat]')).toBeNull();
  });

  it('reads a dotted or short-named event as words and writes a big page count with separators', async () => {
    window.localStorage.clear();

    listed.items = [
      bot('b6', 'channel.chat.message', '2026-10-01T09:50:00Z'),
      bot('b5', 'ChatMessageReceivedEvent', '2026-10-01T09:40:00Z'),
      bot('b4', 'EventSubNotificationJournaledEvent', '2026-10-01T09:30:00Z'),
      bot('b3', 'LedgerEntryRecordedEvent', '2026-10-01T09:20:00Z'),
      bot('b2', 'TtsUtteranceDispatchedEvent', '2026-10-01T09:10:00Z'),
      bot('b1', 'SomeFutureThingEvent', '2026-10-01T09:00:00Z'),
    ];

    listed.total = 1_423_155;
    window.localStorage.setItem('nnz.event-journal.housekeeping', 'true');
    await open();

    const what = [...document.body.querySelectorAll('[data-what]')].map((cell) => cell.textContent);

    expect(what).toEqual([
      'Twitch notice: chat message',
      'Chat message',
      'Twitch notice saved',
      'Points balance changed',
      'Read a message aloud',
      'Some future thing',
    ]);

    expect(document.body.querySelector('[role="status"]')!.textContent).toContain('56,927');
  });

  it('names each date field and the source filter so neither reads as the other', async () => {
    window.localStorage.clear();
    await open();

    expect(q('[data-filter-from]').closest('label')!.textContent).toContain('From');
    expect(q('[data-filter-to]').closest('label')!.textContent).toContain('To');
    expect(q('[data-filter-source]').textContent).toContain('Source');
    expect(q('[data-filter-source]').textContent).not.toContain('From');
  });

  it('offers clear filters only while a filter is set, as an outline button', async () => {
    window.localStorage.clear();
    await open();

    expect(document.body.querySelector('[data-clear-filters]')).toBeNull();

    mounted?.unmount();
    window.localStorage.setItem('nnz.event-journal.source', 'eventsub');
    await open();

    expect(q('[data-clear-filters]').dataset.variant).toBe('outline');
  });

  it('ignores a remembered free-text type that is not a kind on the list', async () => {
    window.localStorage.clear();
    window.localStorage.setItem('nnz.event-journal.eventType', 'chat.message');
    await open();

    const last = journalCalls().at(-1)!.options.params as { query: Record<string, unknown> };

    expect(last.query.eventType).toBeUndefined();
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

describe('Event journal permissions', () => {
  it('shows none of the write controls to a person who holds no journal key', async () => {
    held.keys = new Set();
    await openTools();

    const writeControls = [
      '[data-export]',
      '[data-import]',
      '[data-import-file]',
      '[data-replay]',
      '[data-rebuild]',
      '[data-import-legacy]',
    ];

    const shown = writeControls.filter(
      (selector) => document.body.querySelector(selector) !== null,
    );

    expect(shown).toEqual([]);
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
