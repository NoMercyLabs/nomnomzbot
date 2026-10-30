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
import { dashboardRoutes } from '@/router/dashboard';

import AdminChannelsView from '../AdminChannelsView.vue';
import en from '../i18n/en.json';
import {
  dashboardAdminChannelConsoleRoute,
  dashboardAdminChannelsRoute,
  dashboardAdminTenantsRoute,
} from '../route';

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

const tenant = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
) => ({
  id,
  name,
  twitchChannelId: `tw-${id}`,
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-06-29T10:00:00Z',
  suspendedAt: null,
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
    tenant('t1', 'stoney_eagle', { isLive: true, billingTierKey: 'pro' }),
    tenant('t2', 'quiet_one', { status: 'suspended', createdAt: '2026-05-01T10:00:00Z' }),
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

async function open(start: string = '/app/admin/channels'): Promise<void> {
  // The frame's sticky footer, where a phone-width page moves its pager.
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');

  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/channels',
        name: 'dashboard-admin-channels',
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

  mounted = mount(AdminChannelsView, {
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
  ...document.body.querySelectorAll<HTMLElement>('[data-channel-row]'),
];
const lastQuery = (): Record<string, unknown> => calls[calls.length - 1]!.query;
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

describe('AdminChannelsView', () => {
  it('renders one row per channel with its name, status, live state and created date', async () => {
    await open();

    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.textContent).toContain('stoney_eagle');
    expect(rows()[0]!.textContent).toContain('Active');
    expect(rows()[0]!.textContent).toContain('Live');
    expect(rows()[0]!.textContent).toContain('Jun 29, 2026');
    expect(rows()[1]!.textContent).toContain('quiet_one');
    expect(rows()[1]!.textContent).toContain('Suspended');
    expect(rows()[1]!.textContent).toContain('Offline');
    expect(rows()[1]!.textContent).toContain('May 1, 2026');
    expect(text()).toContain('2 channels');
  });

  it('labels both filter groups in view; a phone wraps the second group to its own row, never clipped', async () => {
    await open();

    const labels = [...document.body.querySelectorAll('[data-filter-label]')].map((node) =>
      node.textContent?.trim());
    const strip = document.body.querySelector('[data-slot="page-bar"] > div') as HTMLElement;

    expect(labels).toEqual(['Status', 'Live']);
    expect(strip.className).toContain('max-md:flex-wrap');
    expect(strip.className).toContain('md:overflow-x-auto');
    expect(strip.className).not.toMatch(/(^|\s)overflow-x-auto/);
  });

  it('lets the search and the table stay inside the page width on a phone', async () => {
    await open();

    const search = document.body.querySelector('[data-slot="search-field"]') as HTMLElement;
    const card = document.body.querySelector('[data-slot="table-container"]')!
      .closest('[data-slot="panel-card"], .min-h-80') as HTMLElement;

    expect(search.className).not.toContain('mx-4');
    expect(card.className).toContain('min-w-0');
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

  it('names the tier of each channel by its plan name, never by its key', async () => {
    await open();

    expect(rows()[0]!.textContent).toContain('Pro plan');
    expect(rows()[1]!.textContent).toContain('Free plan');
    expect(text()).not.toContain('>pro<');
  });

  it('links the name of each channel to its console', async () => {
    await open();

    const links = rows().map((row) => row.querySelector('a')!.getAttribute('href'));

    expect(links).toEqual(['/app/admin/channels/t1', '/app/admin/channels/t2']);
  });

  it('opens the console of the channel when its row is clicked', async () => {
    await open();
    rows()[1]!.click();
    await settle();

    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels-console');
    expect(router.currentRoute.value.params.channelId).toBe('t2');
  });

  it('keeps the Act as button in the row, and it opens the dialog without leaving the list', async () => {
    await open();
    rows()[0]!.querySelector<HTMLElement>('[data-act-as]')!.click();
    await settle();

    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels');

    expect(document.body.querySelector('[role="dialog"]')!.textContent).toContain(
      'Act as someone in stoney_eagle',
    );
  });

  it('keeps the support contact form under the list', async () => {
    await open();

    const field = document.body.querySelector<HTMLInputElement>(
      'input[placeholder="https://example.com/support"]',
    );

    expect(text()).toContain('Support contact');
    expect(field!.value).toBe('https://example.com/help');
  });

  it('starts with the search of the address, so a copied link reopens the same list', async () => {
    await open('/app/admin/channels?search=stoney');

    expect((document.body.querySelector('input[type="search"]') as HTMLInputElement).value).toBe('stoney');
    expect(calls.some((call) => call.query.search === 'stoney')).toBe(true);
  });

  it('sends the chosen status as the status filter', async () => {
    await open();
    byText('[role="radio"], button', 'Suspended')!.click();
    await settle();

    expect(lastQuery().status).toBe('suspended');
    expect(lastQuery().Page).toBe(1);
  });

  it('keeps the status and live choices after the page is opened again', async () => {
    await open();
    byText('[role="radio"], button', 'Suspended')!.click();
    await settle();
    byText('[role="radio"], button', 'Live')!.click();
    await settle();
    mounted!.unmount();
    mounted = null;
    document.body.innerHTML = '';
    calls.length = 0;

    await open();

    expect(calls[0]!.query.status).toBe('suspended');
    expect(calls[0]!.query.isLive).toBe(true);
    expect(calls[0]!.query.Page).toBe(1);
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

  it('shows the empty text when no channel matches', async () => {
    rowsOnPage = [];
    total = 0;
    await open();

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('No channel matches these filters.');
  });

  it('shows the error state with a retry button when the bot refuses the list', async () => {
    refused = true;
    await open();

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('You may not see the channel list.');
    const before = calls.length;

    refused = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(calls.length).toBeGreaterThan(before);
    expect(rows()).toHaveLength(2);
  });
});

describe('admin channels route', () => {
  const blank = { template: '<div />' };
  const testRouter = (): Router =>
    createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: '/',
          name: 'dashboard-home',
          component: blank,
        },
        { ...dashboardAdminChannelsRoute, path: '/app/admin/channels' },
      ],
    });

  it('sends a non-admin to the home page', async () => {
    queryClient.setQueryData(['auth', 'me'], {
      id: 'u1',
      isAdmin: false,
      platformPermissions: [],
    });

    const guarded = testRouter();

    await guarded.push('/app/admin/channels');

    expect(guarded.currentRoute.value.name).toBe('dashboard-home');
  });

  it('lets an admin in', async () => {
    queryClient.setQueryData(['auth', 'me'], {
      id: 'u1',
      isAdmin: true,
      platformPermissions: ['tenant:read'],
    });

    const guarded = testRouter();

    await guarded.push('/app/admin/channels');

    expect(guarded.currentRoute.value.name).toBe('dashboard-admin-channels');
  });

  it('is registered in the dashboard, so the profile menu link appears', () => {
    const real: Router = createRouter({
      history: createMemoryHistory(),
      routes: [dashboardRoutes],
    });

    expect(real.hasRoute('dashboard-admin-channels')).toBe(true);
  });
});

describe('admin tenants route', () => {
  const blank = { template: '<div />' };
  const testRouter = (): Router =>
    createRouter({
      history: createMemoryHistory(),
      routes: [
        { ...dashboardAdminTenantsRoute, path: '/app/admin/tenants' },
        { ...dashboardAdminChannelsRoute, path: '/app/admin/channels' },
        { ...dashboardAdminChannelConsoleRoute, path: '/app/admin/channels/:channelId/:tab?' },
        {
          path: '/',
          name: 'dashboard-home',
          component: blank,
        },
      ],
    });

  beforeEach(() => {
    queryClient.setQueryData(['auth', 'me'], {
      id: 'u1',
      isAdmin: true,
      platformPermissions: ['tenant:read'],
    });
  });

  it('sends the old address to the merged list with the same query', async () => {
    const redirected = testRouter();

    await redirected.push('/app/admin/tenants?search=stoney&status=suspended');

    expect(redirected.currentRoute.value.name).toBe('dashboard-admin-channels');

    expect(redirected.currentRoute.value.fullPath).toBe(
      '/app/admin/channels?search=stoney&status=suspended',
    );
  });

  it('sends the old route name to the merged list too', async () => {
    const redirected = testRouter();

    await redirected.push({ name: 'dashboard-admin-tenants', query: { search: 'quiet' } });

    expect(redirected.currentRoute.value.fullPath).toBe('/app/admin/channels?search=quiet');
  });

  it('sends an old link to one tenant to the console of that channel', async () => {
    const redirected = testRouter();

    await redirected.push('/app/admin/tenants?tenant=t1');

    expect(redirected.currentRoute.value.name).toBe('dashboard-admin-channels-console');
    expect(redirected.currentRoute.value.params.channelId).toBe('t1');
  });
});
