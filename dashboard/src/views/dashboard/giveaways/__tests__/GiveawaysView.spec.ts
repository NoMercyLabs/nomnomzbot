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

import { chooseTarget, openTargetPicker } from '@/components/layout/__tests__/targetPickerDom';
import { i18n } from '@/i18n';
import { lookup } from '@/lib/array';

import en from '../i18n/en.json';
import GiveawaysView from '../GiveawaysView.vue';

const giveaway = (id: string, title: string, status: string, entryCount: number) => ({
  id,
  title,
  status,
  entryCount,
  entryMode: 'keyword',
  keyword: '!enter',
  winnerCount: 1,
  maxEntriesPerUser: 1,
  excludeModerators: false,
  prizeMode: 'announce',
  prizeFromPot: false,
  requires18Plus: false,
  createdAt: '2026-10-15T18:00:00Z',
});

let giveaways: (ReturnType<typeof giveaway> & { target?: { kind: number; id: string } })[] = [];
let winners: {
  id: string;
  viewerDisplayName: string;
  status: string;
  isRedraw: boolean;
}[] = [];
const posts: string[] = [];
const bodies: Record<string, unknown>[] = [];
let entries: {
  id: string;
  viewerDisplayName: string;
  ticketCount: number;
}[] = [];
let pools: {
  id: string;
  name: string;
  total: number;
  available: number;
  assigned: number;
}[] = [];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      const bodiesByPath = {
        '/api/v{version}/giveaways/{id}/winners': winners,
        '/api/v{version}/giveaways/{id}/entries': entries,
        '/api/v{version}/giveaways/code-pools': pools,
        '/api/v{version}/channels/{channelId}/pipelines': [],
        '/api/v{version}/channels/{channelId}/commands': [{ id: 'c1', name: 'hello' }],
        '/api/v{version}/giveaways/code-pools/{poolId}/blast-radius': {
          categories: [
            {
              categoryKey: 'giveaways',
              count: 2,
              sample: [],
            },
          ],
          isMinimum: false,
        },
        '/api/v{version}/giveaways/{id}/blast-radius': {
          categories: [
            {
              categoryKey: 'giveaway_entries',
              count: 12,
              sample: [],
            },
          ],
          isMinimum: false,
        },
      } as const satisfies Record<string, unknown>;

      return {
        data: { data: lookup(bodiesByPath, path) ?? giveaways },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, init?: { body?: Record<string, unknown> }) => {
      posts.push(path);

      if (init?.body) {
        bodies.push(init.body);
      }

      if (path.endsWith('/draw')) {
        giveaways = giveaways.map((row) =>
          row.status === 'open'
            ? { ...row, status: 'drawn' }
            : row,
        );

        winners = [
          {
            id: 'w1',
            viewerDisplayName: 'Kitte',
            status: 'pending',
            isRedraw: false,
          },
        ];
      }

      if (path.endsWith('/open')) {
        giveaways = giveaways.map((row) =>
          row.status === 'draft'
            ? { ...row, status: 'open' }
            : row,
        );
      }

      if (path.endsWith('/code-pools')) {
        return { data: { data: { id: 'p9' } }, response: { status: 200, ok: true } };
      }

      return { data: {}, response: { status: 200, ok: true } };
    },
    PUT: async (path: string, init: { body: Record<string, unknown> }) => {
      posts.push(`PUT ${path}`);
      bodies.push(init.body);

      return { data: {}, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string) => {
      posts.push(`DELETE ${path}`);
      giveaways = [];

      return { data: {}, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  localStorage.clear();
  posts.length = 0;
  bodies.length = 0;
  winners = [];
  entries = [];
  pools = [];
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
  await new Promise((done) => setTimeout(done, 200));
  await flushPromises();
}

async function open(): Promise<QueryClient> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(GiveawaysView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await settle();

  return queryClient;
}

const rows = (): string[] =>
  Array.from(document.querySelectorAll('[data-giveaway-row]')).map((row) => row.textContent ?? '');

const action = (name: string): HTMLElement | undefined =>
  Array.from(document.querySelectorAll<HTMLElement>('[data-giveaway-action]')).find(
    (button) => button.dataset.giveawayAction === name,
  );

const actionNames = (row: Element | undefined): (string | undefined)[] =>
  Array.from(row?.querySelectorAll<HTMLElement>('[data-giveaway-action]') ?? []).map(
    (button) => button.dataset.giveawayAction,
  );

describe('GiveawaysView', () => {
  it('lists each giveaway with its status and entries, and offers only the steps that fit its status', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'open', 12), giveaway('g2', 'Mug', 'draft', 0)];
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('Summer key');
    expect(rows()[0]).toContain(en.dashboard.giveaways.status.open);
    expect(rows()[0]).toContain('12 entries');
    expect(rows()[1]).toContain(en.dashboard.giveaways.status.draft);

    const [openRow, draftRow] = Array.from(document.querySelectorAll('[data-giveaway-row]'));

    expect(actionNames(openRow)).toEqual(['close', 'draw']);
    expect(actionNames(draftRow)).toEqual(['open']);
  });

  it('draws the winners: posts the draw, flips the row to drawn and lists the winner', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'open', 12)];
    await open();

    action('draw')?.click();
    await settle();

    expect(posts).toEqual(['/api/v{version}/giveaways/{id}/draw']);
    expect(rows()[0]).toContain(en.dashboard.giveaways.status.drawn);
    expect(action('draw')).toBeUndefined();
    expect(document.querySelector('[data-winner-row]')?.textContent).toContain('Kitte');
  });

  it('opens a draft giveaway', async () => {
    giveaways = [giveaway('g2', 'Mug', 'draft', 0)];
    await open();

    action('open')?.click();
    await settle();

    expect(posts).toEqual(['/api/v{version}/giveaways/{id}/open']);
    expect(rows()[0]).toContain(en.dashboard.giveaways.status.open);
  });

  it('redraws a winner through the redraw endpoint', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'drawn', 12)];

    winners = [
      {
        id: 'w1',
        viewerDisplayName: 'Kitte',
        status: 'pending',
        isRedraw: false,
      },
    ];

    await open();

    document.querySelector<HTMLElement>('[data-winner-redraw]')?.click();
    await settle();

    expect(posts).toEqual(['/api/v{version}/giveaways/{id}/winners/{winnerId}/redraw']);
  });

  it('lists the entries of the giveaway in focus and the code pools', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'open', 2)];

    entries = [
      {
        id: 'e1',
        viewerDisplayName: 'Kitte',
        ticketCount: 1,
      },
      {
        id: 'e2',
        viewerDisplayName: 'Bamo',
        ticketCount: 3,
      },
    ];

    pools = [
      {
        id: 'p1',
        name: 'Steam keys',
        total: 10,
        available: 7,
        assigned: 3,
      },
    ];

    await open();

    const entryTexts = Array.from(document.querySelectorAll('[data-entry-row]')).map(
      (row) => row.textContent,
    );

    expect(entryTexts).toHaveLength(2);
    expect(entryTexts[1]).toContain('Bamo');
    expect(entryTexts[1]).toContain('3');
    expect(document.querySelector('[data-pool-row]')?.textContent).toContain('Steam keys');
    expect(document.querySelector('[data-pool-row]')?.textContent).toContain('7');
  });

  it('makes a code pool with its codes from the pools panel: the pool first, then the codes into it', async () => {
    await open();

    document.querySelector<HTMLElement>('[data-pool-new]')?.click();
    await settle();
    await fill('[data-pool-name]', 'Steam keys');
    await fill('[data-pool-description]', 'Giveaway summer');
    await fill('[data-pool-codes]', 'AAAA-1111\n\n BBBB-2222 \nAAAA-1111');
    submitDialog();
    await settle();

    expect(posts).toEqual([
      '/api/v{version}/giveaways/code-pools',
      '/api/v{version}/giveaways/code-pools/{poolId}/codes',
    ]);

    expect(bodies[0]).toEqual({ name: 'Steam keys', description: 'Giveaway summer' });
    expect(bodies[1]).toEqual({ codes: [{ code: 'AAAA-1111' }, { code: 'BBBB-2222' }] });
  });

  it('keeps the pool dialog open and names the problem when the pool has no name', async () => {
    await open();

    document.querySelector<HTMLElement>('[data-pool-new]')?.click();
    await settle();
    submitDialog();
    await settle();

    expect(posts).toEqual([]);

    expect(document.body.textContent).toContain(
      en.dashboard.giveaways.pools.dialog.problem.nameRequired,
    );
  });

  it('does not claim live updates are missing and does not poll: the server pushes GiveawayChanged', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'open', 2)];
    const queryClient = await open();

    expect(document.querySelector('[data-not-available]')).toBeNull();
    expect(document.body.textContent).not.toContain('Live updates are not available');
    expect(document.body.textContent).not.toContain('not available here yet');

    const queries = queryClient
      .getQueryCache()
      .findAll({ queryKey: ['channels', 'c1', 'giveaways'] });

    expect(queries.length).toBeGreaterThan(0);

    for (const query of queries) {
      expect(
        query.observers.every((observer) => observer.options.refetchInterval === undefined),
      ).toBe(true);
    }
  });

  it('creates a giveaway from the dialog with the typed title, keyword and winner count', async () => {
    giveaways = [];
    await open();

    document.querySelector<HTMLElement>('[data-giveaway-new]')?.click();
    await settle();
    await fill('[data-giveaway-title]', 'Mug giveaway');
    await fill('[data-giveaway-keyword]', '!mug');
    await fill('[data-giveaway-winners]', '2');
    submitDialog();
    await settle();

    expect(posts).toEqual(['/api/v{version}/giveaways']);

    expect(bodies[0]).toMatchObject({
      title: 'Mug giveaway',
      entryMode: 'keyword',
      keyword: '!mug',
      winnerCount: 2,
    });
  });

  it('saves every setting of the dialog: entry cost, limits, rules, ticket weights, claim window and prize', async () => {
    giveaways = [];
    await open();

    document.querySelector<HTMLElement>('[data-giveaway-new]')?.click();
    await settle();
    await fill('[data-giveaway-title]', 'Gold giveaway');
    await fill('[data-giveaway-entry-cost]', '50');
    await fill('[data-giveaway-max-entries]', '3');
    await fill('[data-giveaway-field="minWatchMinutes"]', '120');
    await fill('[data-giveaway-field="subT1"]', '2');
    await fill('[data-giveaway-field="vip"]', '5');
    await fill('[data-giveaway-claim-window]', '15');
    document.querySelector<HTMLElement>('[data-giveaway-require-sub] [role="switch"]')?.click();

    document
      .querySelector<HTMLElement>('[data-giveaway-exclude-moderators] [role="switch"]')
      ?.click();

    document
      .querySelector<HTMLElement>('[data-giveaway-requires-18-plus] [role="switch"]')
      ?.click();

    await settle();
    submitDialog();
    await settle();

    expect(posts).toEqual(['/api/v{version}/giveaways']);

    expect(bodies[0]).toMatchObject({
      title: 'Gold giveaway',
      entryCost: 50,
      maxEntriesPerUser: 3,
      eligibilityJson: JSON.stringify({ require_sub: true, min_watch_minutes: 120 }),
      weightingJson: JSON.stringify({
        sub_t1: 2,
        sub_t2: 1,
        sub_t3: 1,
        vip: 5,
      }),
      claimWindowMinutes: 15,
      excludeModerators: true,
      requires18Plus: true,
      prizeMode: 'announce',
      prizeCurrencyAmount: null,
      scheduledCloseAt: null,
    });
  });

  it('refuses a ticket count below 1 and says why, without saving', async () => {
    giveaways = [];
    await open();

    document.querySelector<HTMLElement>('[data-giveaway-new]')?.click();
    await settle();
    await fill('[data-giveaway-title]', 'Bad tickets');
    await fill('[data-giveaway-field="subT2"]', '0');
    submitDialog();
    await settle();

    expect(posts).toEqual([]);

    expect(document.body.textContent).toContain(
      en.dashboard.giveaways.dialog.problem.ticketsInvalid,
    );
  });

  it('edits a draft giveaway with a PUT that keeps its other settings', async () => {
    giveaways = [giveaway('g2', 'Mug', 'draft', 0)];
    await open();

    document.querySelector<HTMLElement>('[data-giveaway-edit]')?.click();
    await settle();
    await fill('[data-giveaway-title]', 'Mug v2');
    submitDialog();
    await settle();

    expect(posts).toEqual(['PUT /api/v{version}/giveaways/{id}']);

    expect(bodies[0]).toMatchObject({
      title: 'Mug v2',
      prizeMode: 'announce',
      keyword: '!enter',
    });
  });

  it('shows the command a prize runs when a giveaway opens for editing', async () => {
    giveaways = [
      {
        ...giveaway('g3', 'Command prize', 'draft', 0),
        prizeMode: 'pipeline',
        target: { kind: 0, id: 'c1' },
      },
    ];

    await open();
    document.querySelector<HTMLElement>('[data-giveaway-edit]')?.click();
    await settle();

    expect(document.querySelector('[data-target-picker]')!.textContent).toContain('!hello');
  });

  it('sends a command target when a command is chosen as the prize', async () => {
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;

    giveaways = [{ ...giveaway('g4', 'Pick prize', 'draft', 0), prizeMode: 'pipeline' }];
    await open();
    document.querySelector<HTMLElement>('[data-giveaway-edit]')?.click();
    await settle();

    await openTargetPicker();
    await chooseTarget('!hello');
    await settle();
    submitDialog();
    await settle();

    expect(bodies[0]).toMatchObject({ prizeMode: 'pipeline', target: { kind: 0, id: 'c1' } });
  });

  it('shows what a delete would remove, then deletes and drops the row', async () => {
    giveaways = [giveaway('g1', 'Summer key', 'drawn', 12)];
    await open();

    document.querySelector<HTMLElement>('[data-giveaway-delete]')?.click();
    await settle();

    expect(document.body.textContent).toContain('12');
    expect(posts).toEqual([]);

    const confirm = Array.from(document.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === en.dashboard.giveaways.delete.action,
    );

    confirm?.click();
    await settle();

    expect(posts).toEqual(['DELETE /api/v{version}/giveaways/{id}']);
    expect(rows()).toHaveLength(0);
  });

  it('shows what a pool delete would affect, then deletes that pool', async () => {
    pools = [
      {
        id: 'p1',
        name: 'Steam keys',
        total: 10,
        available: 7,
        assigned: 3,
      },
    ];

    await open();

    document.querySelector<HTMLElement>('[data-pool-delete]')?.click();
    await settle();

    expect(document.body.textContent).toContain('Steam keys');
    expect(document.body.textContent).toContain('2');
    expect(posts).toEqual([]);

    const confirm = Array.from(document.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === en.dashboard.giveaways.pools.deleteAction,
    );

    confirm?.click();
    await settle();

    expect(posts).toEqual(['DELETE /api/v{version}/giveaways/code-pools/{poolId}']);
  });
});

// The Input passes its value on after a tick, so typing settles before the next step.
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
