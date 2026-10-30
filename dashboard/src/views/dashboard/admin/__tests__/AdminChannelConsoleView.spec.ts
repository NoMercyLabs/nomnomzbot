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

import AdminChannelConsoleView from '../AdminChannelConsoleView.vue';
import en from '../i18n/en.json';
import { dashboardAdminChannelConsoleRoute } from '../route';

type Options = {
  params?: { path?: Record<string, string>; query?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  options: Options;
};

const calls: Call[] = [];
let missing: boolean = false;

const detail = {
  id: 't1',
  name: 'stoney_eagle',
  twitchChannelId: '1',
  status: 'active',
  suspendedReason: null,
  billingTierKey: 'pro',
  deploymentMode: 'saas',
  ownerUserId: 'u1',
  ownerDisplayName: 'Stoney_Eagle',
  membershipCount: 4,
  createdAt: '2026-06-29T10:00:00Z',
};

const summary = {
  channelName: 'stoney_eagle',
  tierKey: 'pro',
  tierDisplayName: 'Pro plan',
  isLive: true,
  runsLast24Hours: 120,
  failedRunsLast24Hours: 3,
  lastFailedRunId: 55,
  lastFailedRunAt: '2026-10-29T10:00:00Z',
  lastFailedRunError: 'Host call timed out',
};

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => {
  const answer = (method: string, path: string) => {
    if (path.endsWith('/diagnostics'))
      return { data: { data: { broadcasterId: 't1', summary } } } as const satisfies Record<string, unknown>;

    if (path.endsWith('/admin/audit'))
      return {
        data: {
          data: [
            {
              id: 'a1',
              occurredAt: '2026-10-29T09:00:00Z',
              principalType: 'User',
              permission: 'tenant:read',
              targetResource: 'diagnostics:summary',
              justification: null,
              outcome: 'Allowed',
              breakGlass: false,
            },
          ],
          total: 1,
        },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/invoices'))
      return {
        data: { data: [{
          id: 'i1',
          number: 'INV-7',
          status: 'paid',
          amountMinor: 900,
          currency: 'EUR',
        }] },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/members'))
      return {
        data: {
          data: [
            {
              userId: 'u2',
              username: 'quiet_one',
              displayName: 'Quiet_One',
              relation: 'moderator',
              managementRole: 'moderator',
              communityStanding: null,
            },
          ],
          total: 1,
        },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/limits') && method === 'GET')
      return {
        data: {
          data: [
            {
              id: 'l1',
              broadcasterId: 't1',
              limitKey: 'commands.max',
              limitValue: 500,
              reason: 'Partner deal',
              grantedByPrincipalId: 'p1',
              createdAt: '2026-10-01T10:00:00Z',
              expiresAt: null,
            },
          ],
        },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/erase/preview'))
      return {
        data: {
          data: {
            channelName: 'stoney_eagle',
            blastRadius: { categories: [], isMinimum: false },
            externalConsequences: [],
            restoreWindowDays: 30,
            permanentAfterUtc: '2026-11-18T10:00:00Z',
          },
        },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/export'))
      return { data: { data: '{"channel":"stoney_eagle"}' } } as const satisfies Record<string, unknown>;

    if (path.endsWith('/remigrate'))
      return {
        data: { data: { migrationsAppliedThisCall: ['M1'], stillPending: [] } },
      } as const satisfies Record<string, unknown>;

    if (path.endsWith('/admin/tenants/{broadcasterId}'))
      return { data: { data: detail } } as const satisfies Record<string, unknown>;

    if (path.endsWith('/admin/billing/tiers'))
      return {
        data: { data: [{ key: 'pro', displayName: 'Pro plan' }] },
      } as const satisfies Record<string, unknown>;

    return { data: { data: {} } } as const satisfies Record<string, unknown>;
  };

  const handle = async (method: string, path: string, options?: Options) => {
    calls.push({
      method,
      path,
      options: options ?? {},
    });

    if (missing && method === 'GET')
      return { error: { message: 'no' }, response: { status: 404, ok: false } };

    return { ...answer(method, path), response: { status: 200, ok: true } };
  };

  return {
    client: {
      use: () => undefined,
      GET: (path: string, options?: Options) => handle('GET', path, options),
      POST: (path: string, options?: Options) => handle('POST', path, options),
      PUT: (path: string, options?: Options) => handle('PUT', path, options),
      DELETE: (path: string, options?: Options) => handle('DELETE', path, options),
    },
  };
});

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  missing = false;
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

async function open(start: string = '/app/admin/channels/t1'): Promise<void> {
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
        component: AdminChannelConsoleView,
      },
    ],
  });

  await router.push(start);

  mounted = mount(AdminChannelConsoleView, {
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
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );
const sent = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

async function goTo(label: string): Promise<void> {
  byText('[role="tab"]', label)!.dispatchEvent(
    new MouseEvent('mousedown', { button: 0, bubbles: true }),
  );

  await settle();
}

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

describe('AdminChannelConsoleView header', () => {
  it('shows the channel name with its live and status badges, its tier, and one way back to the list', async () => {
    await open();

    const header = document.body.querySelector('[data-console-header]') as HTMLElement;

    expect(header.querySelector('h2')!.textContent).toBe('stoney_eagle');
    expect(header.textContent).toContain('Live');
    expect(header.textContent).toContain('Active');
    expect(header.textContent).toContain('Pro plan');
    expect(document.body.querySelector('header h1')!.textContent).toBe('stoney_eagle');
    expect(document.body.querySelector('[data-slot="page-crumb"]')!.textContent?.trim()).toBe('Channels');
    expect(document.body.querySelectorAll('[data-slot="breadcrumb-item"]')).toHaveLength(0);

    expect(document.body.querySelector('[data-slot="back-link"]')!.textContent?.trim()).toBe('All channels');
  });

  it('keeps Act as as the one filled button of the header and opens the Act as dialog', async () => {
    await open();

    const header = document.body.querySelector('[data-console-header]') as HTMLElement;
    // The default (filled) variant sets no data-variant; every other variant names itself.
    const filled = [
      ...header.querySelectorAll<HTMLElement>('[data-slot="button"]:not([data-variant])'),
    ];

    expect(filled.map((button) => button.textContent?.trim())).toEqual(['Act as']);

    filled[0]!.click();
    await settle();

    expect(document.body.querySelector('[role="dialog"]')!.textContent).toContain(
      'Act as someone in stoney_eagle',
    );
  });

  it('writes the address with the open tab when Copy link is pressed', async () => {
    const writeText = vi.fn<(link: string) => Promise<void>>(async () => undefined);

    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await open('/app/admin/channels/t1/people');

    document.body.querySelector<HTMLElement>('[data-copy-link]')!.click();
    await settle();

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/app/admin/channels/t1/people`);
    expect(text()).toContain('Copied');
  });

  it('copies the summary tab in the address even when the address names no tab', async () => {
    const writeText = vi.fn<(link: string) => Promise<void>>(async () => undefined);

    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await open();

    document.body.querySelector<HTMLElement>('[data-copy-link]')!.click();
    await settle();

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/app/admin/channels/t1/summary`);
  });

  it('shows the error state with a retry when the channel does not load', async () => {
    missing = true;
    await open();

    expect(text()).toContain('The channel could not load.');
    expect(document.body.querySelector('[data-console-header]')).toBeNull();

    missing = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(document.body.querySelector('[data-console-header] h2')!.textContent).toBe('stoney_eagle');
  });
});

describe('AdminChannelConsoleView tabs', () => {
  it('shows exactly the six tabs that have a source today, Summary first', async () => {
    await open();

    const labels = [...document.body.querySelectorAll('[role="tab"]')].map((tab) =>
      tab.textContent?.trim());

    expect(labels).toEqual(['Summary', 'Billing', 'People', 'Limits', 'Audit', 'Data']);
  });

  it('shows the diagnostics summary: the runs, the failures, the last failure and the facts', async () => {
    await open();

    const read = sent('GET', '/diagnostics')!;
    const runs = document.body.querySelector('[data-summary-runs]') as HTMLElement;
    const failed = document.body.querySelector('[data-summary-failed]') as HTMLElement;
    const last = document.body.querySelector('[data-summary-last-failure]') as HTMLElement;

    expect(read.options.params?.path?.broadcasterId).toBe('t1');
    expect(read.options.params?.query?.sections).toBe('summary');
    expect(runs.textContent).toContain('120');
    expect(failed.textContent).toContain('3');
    expect(last.textContent).toContain('Host call timed out');
    expect(last.textContent).toContain('Oct 29, 2026');
    expect(text()).toContain('Stoney_Eagle');
    expect(text()).toContain('Hosted service (restricted)');
    expect(text()).not.toContain('saas');
    expect(text()).toContain('earn money with NomNomzBot');
  });

  it('says so when the channel has no failed run on record', async () => {
    summary.lastFailedRunError = null as unknown as string;
    summary.failedRunsLast24Hours = 0;

    try {
      await open();

      expect(document.body.querySelector('[data-summary-last-failure]')!.textContent).toContain(
        'No failed run on record.',
      );
    } finally {
      summary.lastFailedRunError = 'Host call timed out';
      summary.failedRunsLast24Hours = 3;
    }
  });

  it('changes the address when another tab is picked, and shows that tab', async () => {
    await open();
    await goTo('People');

    expect(router.currentRoute.value.params.tab).toBe('people');
    expect(router.currentRoute.value.params.channelId).toBe('t1');
    expect(text()).toContain('Quiet_One');
    expect(document.body.querySelector('[data-summary-runs]')).toBeNull();
  });

  it('opens the Billing tab when the page loads on its address', async () => {
    await open('/app/admin/channels/t1/billing');

    expect(text()).toContain('Channel billing');
    expect(text()).toContain('INV-7');
    expect(sent('GET', '/invoices')!.options.params?.path?.broadcasterId).toBe('t1');
    expect(document.body.querySelector('[data-summary-runs]')).toBeNull();
    expect(byText('[role="tab"]', 'Billing')!.getAttribute('aria-selected')).toBe('true');
  });

  it('falls back to Summary for a tab it does not know', async () => {
    await open('/app/admin/channels/t1/nonsense');

    expect(document.body.querySelector('[data-summary-runs]')).not.toBeNull();
    expect(byText('[role="tab"]', 'Summary')!.getAttribute('aria-selected')).toBe('true');
  });

  it('lists the audit rows of this channel only', async () => {
    await open('/app/admin/channels/t1/audit');

    const rows = document.body.querySelectorAll('[data-audit-row]');
    const read = sent('GET', '/admin/audit')!;

    expect(rows).toHaveLength(1);
    expect(rows[0]!.textContent).toContain('tenant:read');
    expect(rows[0]!.textContent).toContain('diagnostics:summary');
    expect(read.options.params?.query?.targetBroadcasterId).toBe('t1');
  });
});

describe('AdminChannelConsoleView actions', () => {
  it('shows the members of the channel on the People tab', async () => {
    await open('/app/admin/channels/t1/people');

    expect(text()).toContain('Quiet_One');
    expect(text()).toContain('Moderator');
  });

  it('saves a limit override with a numeric value and clears one by key', async () => {
    await open('/app/admin/channels/t1/limits');

    expect(text()).toContain('commands.max');
    expect(text()).toContain('Partner deal');

    byText('button', 'Add limit')!.click();
    await settle();

    await type('[data-limit-key]', 'pipelines.max');
    await type('[data-limit-value]', '12');
    await type('[data-limit-reason]', 'Trial');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('PUT', '/limits')!.options.body).toEqual({
      limitKey: 'pipelines.max',
      limitValue: 12,
      reason: 'Trial',
      expiresAt: null,
    });

    document.body.querySelector<HTMLElement>('[data-clear-limit="commands.max"]')!.click();
    await settle();

    expect(sent('DELETE', '/limits/{limitKey}')!.options.params?.path?.limitKey).toBe(
      'commands.max',
    );
  });

  it('suspends with the chosen status and the typed reason', async () => {
    await open('/app/admin/channels/t1/data');
    byText('button', 'Suspend')!.click();
    await settle();

    await type('[data-suspend-reason]', 'Abuse reports');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    const call = sent('POST', '/suspend')!;

    expect(call.options.params?.path?.broadcasterId).toBe('t1');
    expect(call.options.body).toEqual({ newStatus: 'suspended', reason: 'Abuse reports' });
  });

  it('forces a re-migration with the typed justification and shows what it applied', async () => {
    await open('/app/admin/channels/t1/data');
    byText('button', 'Re-run migrations')!.click();
    await settle();

    await type('[data-justification]', 'Stuck after deploy');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('POST', '/remigrate')!.options.body).toEqual({
      justification: 'Stuck after deploy',
    });

    expect(text()).toContain('M1');
  });

  it('previews an erase and only erases with a justification', async () => {
    await open('/app/admin/channels/t1/data');
    byText('button', 'Erase channel')!.click();
    await settle();

    expect(sent('GET', '/erase/preview')).toBeDefined();
    expect(text()).toContain('30');

    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();
    expect(sent('POST', '/erase')).toBeUndefined();

    await type('[data-justification]', 'Owner request');
    document.body.querySelector<HTMLElement>('[data-form-action]')!.click();
    await settle();

    expect(sent('POST', '/erase')!.options.body).toEqual({ justification: 'Owner request' });
  });

  it('asks for the export of the channel', async () => {
    URL.createObjectURL = vi.fn<() => string>(() => 'blob:x');
    URL.revokeObjectURL = vi.fn<() => void>();
    await open('/app/admin/channels/t1/data');
    byText('button', 'Export data')!.click();
    await settle();

    expect(sent('GET', '/export')!.options.params?.path?.broadcasterId).toBe('t1');
  });
});

describe('admin channel console route', () => {
  it('is registered in the dashboard beside the list, behind the same admin guard', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-channels-console');

    expect(registered?.path).toBe('admin/channels/:channelId/:tab?');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminChannelConsoleRoute.beforeEnter);
  });
});
