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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminTenantsView from '../AdminTenantsView.vue';
import en from '../i18n/en.json';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let rowsOnPage: Record<string, unknown>[] = [];
let total: number = 0;
let refused: boolean = false;

const channelOf = (
  broadcasterId: string,
  name: string,
  over: Record<string, unknown> = {},
) => ({
  broadcasterId,
  name,
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  isOnboarded: true,
  suspendedAt: null,
  ...over,
}) as const satisfies Record<string, unknown>;

const streamer = (
  userId: string,
  displayName: string,
  channel: Record<string, unknown>,
  over: Record<string, unknown> = {},
) => ({
  userId,
  displayName,
  login: displayName.toLowerCase(),
  kind: 'streamer',
  platforms: ['twitch'],
  lastActive: '2026-10-02T10:00:00Z',
  createdAt: '2026-06-29T10:00:00Z',
  channel,
  ...over,
}) as const satisfies Record<string, unknown>;

const viewer = (
  userId: string,
  displayName: string,
  over: Record<string, unknown> = {},
) => ({
  userId,
  displayName,
  login: displayName.toLowerCase(),
  kind: 'viewer',
  platforms: ['twitch', 'youtube'],
  lastActive: '2026-10-01T10:00:00Z',
  createdAt: '2026-05-01T10:00:00Z',
  channel: null,
  ...over,
}) as const satisfies Record<string, unknown>;

// The signed-in admin holds every platform key, so the Act as button shows. The route guard keeps the real check.
vi.mock('@/composables/usePlatformCan', async (importOriginal) => ({
  ...(await importOriginal<Record<string, unknown>>()),
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({ path, query: { ...options?.params?.query } });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      if (path.endsWith('/admin/billing/tiers')) {
        return {
          data: { data: [
            {
              id: 'k1',
              key: 'pro',
              displayName: 'Pro plan',
            },
            {
              id: 'k2',
              key: 'free',
              displayName: 'Free plan',
            },
          ] },
          response: { status: 200, ok: true },
        };
      }

      if (path.endsWith('/support-contact')) {
        return {
          data: { data: { url: 'https://example.com/help' } },
          response: { status: 200, ok: true },
        };
      }

      return {
        data: {
          data: rowsOnPage,
          total,
          hasMore: total > 25,
          nextPage: 2,
        },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  refused = false;
  total = 2;

  rowsOnPage = [
    streamer('u-str', 'Stoney_Eagle', channelOf('c1', 'stoney_eagle', {
      isLive: true,
      billingTierKey: 'pro',
    })),
    viewer('u-vwr', 'Quiet_One', { lastActive: null }),
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
  vi.restoreAllMocks();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(start: string = '/app/admin/tenants'): Promise<void> {
  // The frame's sticky footer, where a phone-width page moves its pager.
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');

  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/tenants',
        name: 'dashboard-admin-tenants',
        component: blank,
      },
      {
        path: '/app/admin/channels/:channelId/:tab?',
        name: 'dashboard-admin-channels-console',
        component: blank,
      },
    ],
  });

  await router.push(start);

  mounted = mount(AdminTenantsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-tenant-row]'),
];
const lastQuery = (): Record<string, unknown> => calls[calls.length - 1]!.query;
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

// The text of each cell as the desktop table shows it: the stacked-card label of a cell is hidden there.
const valueOf = (cell: HTMLElement): string => {
  const copy = cell.cloneNode(true) as HTMLElement;

  copy.querySelectorAll('[data-cell-label]').forEach((label) => label.remove());

  return copy.textContent?.replace(/\s+/g, ' ').trim() ?? '';
};

const cellsOf = (row: HTMLElement): string[] =>
  [...row.querySelectorAll<HTMLElement>('td')].map(valueOf);

describe('AdminTenantsView', () => {
  it('shows a viewer as Viewer with its platforms and leaves the channel cells empty', async () => {
    await open();

    const [person, kind, platforms, status, live, tier, lastActive, created] = cellsOf(rows()[1]!);

    expect(person).toContain('Quiet_One');
    expect(person).toContain('quiet_one');
    expect(kind).toBe('Viewer');
    expect(platforms).toBe('Twitch, YouTube');
    expect(status).toBe('-');
    expect(live).toBe('-');
    expect(tier).toBe('-');
    expect(lastActive).toBe('Never');
    expect(created).toBe('May 1, 2026');
    expect(rows()[1]!.textContent).not.toContain('Active');
    expect(rows()[1]!.textContent).not.toContain('Offline');
  });

  it('shows a streamer as Streamer with its platform, live state, status and tier name', async () => {
    await open();

    const [person, kind, platforms, status, live, tier, lastActive, created] = cellsOf(rows()[0]!);

    expect(person).toContain('Stoney_Eagle');
    expect(kind).toBe('Streamer');
    expect(platforms).toBe('Twitch');
    expect(status).toBe('Active');
    expect(live).toBe('Live');
    expect(tier).toBe('Pro plan');
    expect(lastActive).toBe('Oct 2, 2026');
    expect(created).toBe('Jun 29, 2026');
    expect(text()).toContain('2 tenants');
  });

  it('asks the tenants endpoint for the first page without any filter', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/tenants');
    expect(calls[0]!.query).toEqual({ Page: 1, Take: 25 });
  });

  it('opens the channel console of a streamer when its row is clicked', async () => {
    await open();
    rows()[0]!.click();
    await settle();

    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels-console');
    expect(router.currentRoute.value.params.channelId).toBe('c1');
  });

  it('links the name of a streamer to its console by the channel id', async () => {
    await open();

    expect(rows()[0]!.querySelector('a')!.getAttribute('href')).toBe('/app/admin/channels/c1');
    expect(rows()[1]!.querySelector('a')).toBeNull();
  });

  it('opens the detail of a viewer with its platforms, last active and created date', async () => {
    rowsOnPage = [viewer('u-vwr', 'Quiet_One')];
    await open();
    rows()[0]!.click();
    await settle();

    const detail = document.body.querySelector<HTMLElement>('[data-tenant-detail]')!;

    expect(router.currentRoute.value.name).toBe('dashboard-admin-tenants');
    expect(router.currentRoute.value.query.user).toBe('u-vwr');
    expect(detail.textContent).toContain('Quiet_One');
    expect(detail.textContent).toContain('Viewer');
    expect(detail.textContent).toContain('Twitch, YouTube');
    expect(detail.textContent).toContain('Oct 1, 2026');
    expect(detail.textContent).toContain('May 1, 2026');
  });

  it('opens the detail of the viewer named in the address', async () => {
    await open('/app/admin/tenants?user=u-vwr');

    expect(document.body.querySelector('[data-tenant-detail]')!.textContent).toContain('Quiet_One');
  });

  it('keeps the Act as button on the streamer row only, and it opens the dialog without leaving the list', async () => {
    await open();

    expect(rows()[1]!.querySelector('[data-act-as]')).toBeNull();

    rows()[0]!.querySelector<HTMLElement>('[data-act-as]')!.click();
    await settle();

    expect(router.currentRoute.value.name).toBe('dashboard-admin-tenants');

    expect(document.body.querySelector('[role="dialog"]')!.textContent).toContain(
      'Act as someone in stoney_eagle',
    );
  });

  it('labels the three filter groups in view; a phone wraps them to their own rows, never clipped', async () => {
    await open();

    const labels = [...document.body.querySelectorAll('[data-filter-label]')].map((node) =>
      node.textContent?.trim());
    const strip = document.body.querySelector('[data-slot="page-bar"] > div') as HTMLElement;

    expect(labels).toEqual(['Kind', 'Status', 'Live']);
    expect(strip.className).toContain('max-md:flex-wrap');
    expect(strip.className).toContain('md:overflow-x-auto');
    expect(strip.className).not.toMatch(/(^|\s)overflow-x-auto/);
  });

  it('sends the chosen kind as the kind filter and goes back to page 1', async () => {
    total = 60;
    await open();
    byText('button', 'Next')!.click();
    await settle();
    expect(lastQuery().Page).toBe(2);

    byText('[role="radio"], button', 'Viewer')!.click();
    await settle();

    expect(lastQuery().kind).toBe('viewer');
    expect(lastQuery().Page).toBe(1);
  });

  it('sends the chosen status as the status filter', async () => {
    await open();
    byText('[role="radio"], button', 'Suspended')!.click();
    await settle();

    expect(lastQuery().status).toBe('suspended');
    expect(lastQuery().Page).toBe(1);
  });

  it('sends the live choice as isLive', async () => {
    await open();
    byText('[role="radio"], button', 'Live')!.click();
    await settle();

    expect(lastQuery().isLive).toBe(true);

    byText('[role="radio"], button', 'Offline')!.click();
    await settle();

    expect(lastQuery().isLive).toBe(false);
  });

  it('keeps the kind, status and live choices after the page is opened again', async () => {
    await open();
    byText('[role="radio"], button', 'Streamer')!.click();
    await settle();
    byText('[role="radio"], button', 'Suspended')!.click();
    await settle();
    byText('[role="radio"], button', 'Live')!.click();
    await settle();
    mounted!.unmount();
    mounted = null;
    document.body.innerHTML = '';
    calls.length = 0;

    await open();

    expect(calls[0]!.query.kind).toBe('streamer');
    expect(calls[0]!.query.status).toBe('suspended');
    expect(calls[0]!.query.isLive).toBe(true);
    expect(calls[0]!.query.Page).toBe(1);
  });

  it('refetches with the typed search after the pause and goes back to page 1', async () => {
    total = 60;
    await open();
    byText('button', 'Next')!.click();
    await settle();
    expect(lastQuery().Page).toBe(2);

    const input = document.body.querySelector('input[type="search"]') as HTMLInputElement;

    input.value = 'stoney';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    const before = calls.length;

    expect(calls.some((call) => call.query.search === 'stoney')).toBe(false);

    await new Promise((done) => setTimeout(done, 450));
    await settle();
    expect(calls.length).toBeGreaterThan(before);
    expect(lastQuery().search).toBe('stoney');
    expect(lastQuery().Page).toBe(1);
  });

  it('starts with the search of the address, so a copied link reopens the same list', async () => {
    await open('/app/admin/tenants?search=stoney');

    expect((document.body.querySelector('input[type="search"]') as HTMLInputElement).value).toBe('stoney');
    expect(calls.some((call) => call.query.search === 'stoney')).toBe(true);
  });

  it('asks for page 2 when Next is pressed and shows the page count', async () => {
    total = 60;
    await open();
    expect(text()).toContain('Page 1 of 3');

    byText('button', 'Next')!.click();
    await settle();

    expect(lastQuery().Page).toBe(2);
    expect(lastQuery().Take).toBe(25);
    expect(text()).toContain('Page 2 of 3');
  });

  it('keeps the support contact form under the list', async () => {
    await open();

    const field = document.body.querySelector<HTMLInputElement>(
      'input[placeholder="https://example.com/support"]',
    );

    expect(text()).toContain('Support contact');
    expect(field!.value).toBe('https://example.com/help');
  });

  it('shows the empty text when no tenant matches', async () => {
    rowsOnPage = [];
    total = 0;
    await open();

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('No tenant matches these filters.');
  });

  it('shows the forbidden message with a retry button when the bot refuses the list', async () => {
    refused = true;
    await open();

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('You may not see the tenant list.');
    const before = calls.length;

    refused = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(calls.length).toBeGreaterThan(before);
    expect(rows()).toHaveLength(2);
  });
});
