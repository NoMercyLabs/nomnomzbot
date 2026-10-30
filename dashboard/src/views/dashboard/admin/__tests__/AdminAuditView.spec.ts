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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminAuditView from '../AdminAuditView.vue';
import en from '../i18n/en.json';
import { dashboardAdminAuditRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };

const calls: Call[] = [];
let entries: Record<string, unknown>[] = [];
let total: number = 0;
let refused: boolean = false;

// Two people from the tenants list: a viewer who signed in (no channel) and a streamer. The user id and the
// broadcaster id differ on purpose: the operator filter takes the first, the channel filter the second.
const ada = {
  userId: 'U1',
  displayName: 'Ada Lovelace',
  login: 'ada',
  kind: 'viewer',
  platforms: ['twitch'],
  channel: null,
};

const chanNel = {
  userId: 'U2',
  displayName: 'Chan Nel',
  login: 'channel',
  kind: 'streamer',
  platforms: ['twitch'],
  channel: {
    broadcasterId: 'T1',
    name: 'Chan Nel',
    status: 'active',
  },
};

async function answer(path: string, options?: Options) {
  calls.push({ path, query: { ...options?.params?.query } });

  // One endpoint answers both pickers: the operator asks for any person, the channel asks for streamers.
  if (path.endsWith('/admin/tenants')) {
    const streamers = options?.params?.query?.kind === 'streamer';

    return {
      data: { data: streamers
        ? [chanNel]
        : [ada, chanNel], total: 1 },
      response: { status: 200, ok: true },
    };
  }

  if (refused)
    return { error: { message: 'no' }, response: { status: 403, ok: false } };

  return {
    data: {
      data: entries,
      total,
      hasMore: false,
    },
    response: { status: 200, ok: true },
  };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: (path: string, options?: Options) => answer(path, options),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  calls.length = 0;
  refused = false;
  total = 60;

  entries = [
    {
      id: 2,
      principalId: 'p1',
      principalType: 'Employee',
      permission: 'tenant:suspend',
      targetBroadcasterId: 'b1',
      targetResource: null,
      justification: 'Abuse report',
      breakGlass: true,
      outcome: 'Allowed',
      occurredAt: '2026-10-18T10:00:00Z',
    },
    {
      id: 1,
      principalId: 'p2',
      principalType: 'Employee',
      permission: 'billing:write',
      targetBroadcasterId: null,
      targetResource: null,
      justification: null,
      breakGlass: false,
      outcome: 'Denied',
      occurredAt: '2026-10-18T09:00:00Z',
    },
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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

async function open(): Promise<void> {
  // The frame's sticky footer, where a phone-width page moves its pager.
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');

  mounted = mount(AdminAuditView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-audit-row]'),
];
const lastQuery = (): Record<string, unknown> =>
  calls.filter((call) => call.path.endsWith('/admin/audit')).at(-1)!.query;

async function type(label: string, value: string): Promise<void> {
  const field = [...document.body.querySelectorAll('label')].find((entry) =>
    entry.textContent?.includes(label),
  );
  const input = field?.parentElement?.querySelector('input') as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input'));

  await settle();
}

describe('AdminAuditView', () => {
  it('renders one row per audit entry with permission, reason and outcome', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/audit');
    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.textContent).toContain('tenant:suspend');
    expect(rows()[0]!.textContent).toContain('Abuse report');
    expect(rows()[0]!.textContent).toContain('Allowed');
    expect(rows()[1]!.textContent).toContain('Denied');
  });

  it('on a phone stacks each entry as a card with the time as its title and every column as a labeled line', async () => {
    await open();

    const labels = [...rows()[0]!.querySelectorAll('[data-cell-label]')].map((label) => label.textContent?.trim());

    expect(rows()[0]!.className).toContain('max-md:flex-col');
    expect(rows()[0]!.querySelector('[data-stack=title]')).not.toBeNull();
    expect(labels).toHaveLength(5);
  });

  it('flags a break-glass row and only that one', async () => {
    await open();

    expect(rows()[0]!.querySelector('[data-break-glass]')).not.toBeNull();
    expect(rows()[1]!.querySelector('[data-break-glass]')).toBeNull();
  });

  it('sends no filter until one is typed, then sends the permission filter from page one', async () => {
    await open();

    expect(lastQuery().permission).toBeUndefined();
    expect(lastQuery().Page).toBe(1);

    await type('Permission', 'billing:write');

    expect(lastQuery().permission).toBe('billing:write');
    expect(lastQuery().Page).toBe(1);
  });

  it('finds an operator by name and filters on the id of the pick, never on typed text', async () => {
    await open();

    await type('Operator', 'ada');

    const option = document.body.querySelector<HTMLElement>('[data-picker-option]')!;

    expect(option.textContent).toContain('Ada Lovelace');
    expect(option.textContent).toContain('ada');

    // The operator list is every person who joined, so it names no kind.
    const asked = calls.filter((call) => call.path.endsWith('/admin/tenants')).at(-1)!.query;

    expect(asked.search).toBe('ada');
    expect(asked.kind).toBeUndefined();

    option.click();
    await settle();

    expect(lastQuery().principalId).toBe('U1');
    expect(document.body.querySelector('[data-picker-picked]')?.textContent).toContain('Ada Lovelace');
    expect(document.body.querySelector('[data-picker-picked]')?.textContent).not.toContain('U1');
  });

  it('finds a channel by name and clears the pick back to no filter', async () => {
    await open();

    await type('Channel', 'chan');

    const option = document.body.querySelector<HTMLElement>('[data-picker-option]')!;

    // Only a streamer owns a channel, so the channel list asks for streamers.
    const asked = calls.filter((call) => call.path.endsWith('/admin/tenants')).at(-1)!.query;

    expect(asked.kind).toBe('streamer');
    expect(asked.search).toBe('chan');
    expect(option.textContent).toContain('Chan Nel');

    option.click();
    await settle();

    expect(lastQuery().targetBroadcasterId).toBe('T1');

    document.body.querySelector<HTMLElement>('[data-picker-clear]')!.click();
    await settle();

    expect(lastQuery().targetBroadcasterId).toBeUndefined();
  });

  it('shows no raw id in a row: the operator kind and the target resource stand in', async () => {
    entries[0] = {
      ...entries[0],
      principalId: '01KWA6X0E7FMY8PRY6WTZAW0M3',
      targetBroadcasterId: '01KWA6X0E7FMY8PRY6WTZAW0M4',
    };

    await open();

    expect(rows()[0]!.textContent).not.toContain('01KWA6X0E7FMY8PRY6WTZAW0M3');
    expect(rows()[0]!.textContent).not.toContain('01KWA6X0E7FMY8PRY6WTZAW0M4');
    expect(rows()[0]!.textContent).toContain('Employee');
  });

  it('turns a day range into a whole-day window', async () => {
    await open();

    await type('From day', '2026-10-01');
    await type('To day', '2026-10-02');

    expect(lastQuery().from).toBe('2026-10-01T00:00:00Z');
    expect(lastQuery().to).toBe('2026-10-02T23:59:59Z');
  });

  it('pages forward and sends the next page', async () => {
    await open();

    const next = [...document.body.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Next'),
    ) as HTMLButtonElement;

    next.click();
    await settle();

    expect(lastQuery().Page).toBe(2);
    expect(text()).toContain('Page 2 of 3');
  });

  it('goes back to page one when a filter changes', async () => {
    await open();

    const next = [...document.body.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Next'),
    ) as HTMLButtonElement;

    next.click();
    await settle();
    await type('Permission', 'x');

    expect(lastQuery().Page).toBe(1);
  });

  it('shows the forbidden message when the desk refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not read the audit log.');
    expect(rows()).toHaveLength(0);
  });

  it('reloads the log when the hub says a new audit row was written', async () => {
    await open();

    const before = calls.length;
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveAuditChanged')!;

    handler.handle(
      { queries, channelId: null },
      {
        principalId: 'p1',
        permission: 'tenant:suspend',
        outcome: 'Allowed',
        targetBroadcasterId: null,
      } as never,
    );

    await settle();

    expect(calls.length).toBeGreaterThan(before);
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-audit-log');

    expect(registered?.path).toBe('admin/audit-log');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminAuditRoute.beforeEnter);
  });
});
