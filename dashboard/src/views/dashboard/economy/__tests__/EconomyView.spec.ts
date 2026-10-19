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

import { earningRulesKey } from '@/api/economy';
import { i18n } from '@/i18n';

import EconomyView from '../EconomyView.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  path_: Record<string, unknown> | undefined;
  body: Record<string, unknown> | undefined;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
// What the server answers for a channel with no saved currency row: the one set of defaults.
const defaultCurrency = {
  id: '00000000000000000000000000',
  currencyName: 'points',
  currencyNamePlural: 'points',
  iconUrl: null,
  isEnabled: false,
  startingBalance: 0,
  maxBalance: null,
  decimalPlaces: 0,
} as const satisfies Row;
let config: Row | null = null;
let refusal: Row | null = null;
let rules: Row[] = [];
let jars: Row[] = [];
let catalog: Row[] = [];
let boards: Row[] = [];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const rule = (id: string, source: string, overrides: Row = {}) =>
  ({
    id,
    source,
    isEnabled: true,
    rate: 5,
    unitWindowSeconds: null,
    perWindowCap: null,
    perStreamCap: null,
    minRoleLevel: null,
    configSchemaVersion: 1,
    bonusConfig: null,
    ...overrides,
  }) as const satisfies Row;

const jar = (id: string, name: string, overrides: Row = {}) =>
  ({
    id,
    ownerBroadcasterId: 'c1',
    name,
    description: null,
    goalAmount: 1000,
    balance: 250,
    iconUrl: null,
    isOpen: true,
    maxWithdrawalPerChannel: null,
    createdAt: '2026-10-01T10:00:00Z',
    ...overrides,
  }) as const satisfies Row;

function read(path: string): Answer {
  if (path.endsWith('/economy/config'))
    return ok(config);

  if (path.endsWith('/earning-rules'))
    return ok(rules);

  if (path.endsWith('/jars'))
    return ok(jars);

  if (path.endsWith('/economy/catalog'))
    return ok(catalog);

  if (path.endsWith('/leaderboards/configs'))
    return ok(boards);

  return ok(null);
}

function write(method: string, path: string, options?: Options): Answer {
  const params = options?.params?.path;
  const body = options?.body ?? {};

  calls.push({
    method,
    path,
    path_: params,
    body: options?.body,
  });

  if (method === 'PUT' && path.endsWith('/economy/config') && refusal !== null) {
    return { error: refusal, response: { status: 400, ok: false } };
  }

  if (method === 'PUT' && path.endsWith('/economy/config')) {
    config = { id: 'cfg', ...body };
  }

  if (method === 'PUT' && path.endsWith('/earning-rules')) {
    const known = rules.some((row) => row.source === body.source);

    rules = known
      ? rules.map((row) =>
          row.source === body.source
            ? { ...row, ...body }
            : row)
      : [...rules, rule('new', String(body.source), body)];
  }

  if (method === 'DELETE' && path.endsWith('{ruleId}')) {
    rules = rules.filter((row) => row.id !== params?.ruleId);
  }

  if (method === 'POST' && path.endsWith('/economy/catalog')) {
    catalog = [
      ...catalog,
      {
        id: 'i-new',
        cost: 0,
        isEnabled: true,
        ...body,
      },
    ];
  }

  if (method === 'PUT' && path.endsWith('/leaderboards/configs')) {
    boards = [...boards, { id: 'b-new', ...body }];
  }

  if (method === 'POST' && path.endsWith('/jars')) {
    const created = jar('j-new', String(body.name), body);

    jars = [...jars, created];
  }

  if (method === 'PATCH') {
    jars = jars.map((row) =>
      row.id === params?.jarId
        ? { ...row, ...body }
        : row);
  }

  if (method === 'DELETE' && path.endsWith('{jarId}')) {
    jars = jars.filter((row) => row.id !== params?.jarId);
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const allKeys = [
  'economy:config:read',
  'economy:config:write',
  'economy:earning-rules:read',
  'economy:earning-rules:write',
  'economy:earning-rules:delete',
  'economy:jars:read',
  'economy:jars:create',
  'economy:jars:update',
  'economy:jars:invite',
  'economy:jars:delete',
  'economy:catalog:read',
  'economy:catalog:create',
  'economy:catalog:update',
  'economy:catalog:delete',
  'economy:leaderboards:config:read',
  'economy:leaderboards:config:write',
  'economy:leaderboards:config:delete',
];
const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  access.held = allKeys;
  localStorage.clear();
  calls.length = 0;
  config = { ...defaultCurrency };
  refusal = null;
  rules = [rule('r1', 'ChatMessage'), rule('r2', 'Follow', { isEnabled: false, rate: 50 })];
  jars = [jar('j1', 'New camera')];
  catalog = [];
  boards = [];
  pinia = createPinia();
  setActivePinia(pinia);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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

async function open(view: string): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/economy',
        name: 'dashboard-economy',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(`/app/economy?view=${view}`);

  mounted = mount(EconomyView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, router, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const buttonByText = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement;
const field = (label: string): HTMLInputElement =>
  document.body.querySelector(`[aria-label="${label}"]`) as HTMLInputElement;
const rowOf = (kind: string, name: string): HTMLElement =>
  document.body.querySelector(`[data-${kind}-name="${name}"]`) as HTMLElement;
const names = (kind: string): (string | undefined)[] =>
  [...document.body.querySelectorAll<HTMLElement>(`[data-${kind}-row]`)].map(
    (row) => row.getAttribute(`data-${kind}-name`) ?? undefined,
  );
const writes = (method: string): Call[] => calls.filter((call) => call.method === method);

async function type(el: HTMLInputElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const tabLabels = (): string[] =>
  [...document.body.querySelectorAll<HTMLElement>('[role="tab"]')].map(
    (tab) => tab.textContent?.trim() ?? '',
  );
const present = (selector: string): boolean => document.body.querySelector(selector) !== null;

describe('Economy page gating', () => {
  it('lists only the tabs whose read key is held', async () => {
    access.held = ['economy:jars:read', 'economy:catalog:read'];

    await open('jars');

    const { jars: jarsTab, catalog: catalogTab } = en.dashboard.economy.tabs;

    expect(tabLabels()).toEqual([jarsTab, catalogTab]);
  });

  it('shows no create button and no row actions for read-only keys', async () => {
    access.held = ['economy:earning-rules:read', 'economy:jars:read'];

    await open('rules');

    expect(buttonByText(en.dashboard.economy.rules.new)).toBeUndefined();
    expect(present('[data-rule-edit]')).toBe(false);
    expect(present('[data-rule-delete]')).toBe(false);
    expect(present('[data-rule-row] [role="switch"]')).toBe(false);
    expect(present('[data-rule-row] [data-slot="enabled-state"]')).toBe(true);
  });

  it('shows the jar, catalog and currency state as a badge instead of a switch without write', async () => {
    access.held = ['economy:jars:read', 'economy:catalog:read', 'economy:config:read'];

    catalog = [
      {
        id: 'i1',
        name: 'Hat',
        cost: 5,
        isEnabled: true,
      },
    ];

    await open('jars');

    expect(present('[data-jar-row] [role="switch"]')).toBe(false);
    expect(present('[data-jar-row] [data-slot="enabled-state"]')).toBe(true);

    await open('catalog');

    const itemState = document.body.querySelector('[data-catalog-row] [data-slot="enabled-state"]');

    expect(present('[data-catalog-row] [role="switch"]')).toBe(false);
    expect(itemState?.getAttribute('data-state')).toBe('on');

    await open('currency');

    expect(present('[data-currency-form] [role="switch"]')).toBe(false);
    expect(present('[data-currency-form] [data-slot="enabled-state"]')).toBe(true);
  });

  it('shows the switches when the write keys are held', async () => {
    catalog = [
      {
        id: 'i1',
        name: 'Hat',
        cost: 5,
        isEnabled: true,
      },
    ];

    await open('catalog');

    expect(present('[data-catalog-row] [role="switch"]')).toBe(true);
    expect(present('[data-slot="enabled-state"]')).toBe(false);

    await open('currency');

    expect(present('[data-currency-form] [role="switch"]')).toBe(true);
  });

  it('hides the jar invite and delete actions without their keys', async () => {
    access.held = ['economy:jars:read'];

    await open('jars');

    expect(buttonByText(en.dashboard.economy.jars.new)).toBeUndefined();
    expect(present('[data-jar-invite]')).toBe(false);
    expect(present('[data-jar-delete]')).toBe(false);
  });

  it('hides catalog and leaderboard edit and delete without their keys', async () => {
    access.held = ['economy:catalog:read', 'economy:leaderboards:config:read'];

    catalog = [
      {
        id: 'i1',
        name: 'Hat',
        cost: 5,
        isEnabled: true,
      },
    ];

    await open('catalog');

    expect(present('[data-catalog-row]')).toBe(true);
    expect(present('[data-catalog-edit]')).toBe(false);
    expect(present('[data-catalog-delete]')).toBe(false);
    expect(buttonByText(en.dashboard.economy.catalog.new)).toBeUndefined();
  });

  it('hides the currency save button without the write key', async () => {
    access.held = ['economy:config:read'];

    await open('currency');

    expect(present('[data-currency-form]')).toBe(true);
    expect(buttonByText('Save currency')).toBeUndefined();
  });

  it('shows the create buttons and row actions when the keys are held', async () => {
    await open('rules');

    expect(buttonByText(en.dashboard.economy.rules.new)).toBeDefined();
    expect(present('[data-rule-edit]')).toBe(true);
    expect(present('[data-rule-delete]')).toBe(true);
  });
});

describe('Economy page', () => {
  it('sets up the currency the channel has none of, and saves the typed values', async () => {
    await open('currency');
    await type(field('Currency name'), 'Nomz');
    await type(field('Starting balance'), '100');
    await click(buttonByText('Save currency'));

    expect(writes('PUT')).toHaveLength(1);

    expect(writes('PUT')[0]!.body).toMatchObject({
      currencyName: 'Nomz',
      startingBalance: 100,
      isEnabled: false,
    });

    expect(config?.currencyName).toBe('Nomz');
  });

  it('shows the defaults the server sends for a channel with no currency row', async () => {
    config = {
      ...defaultCurrency,
      currencyName: 'server-default',
      startingBalance: 7,
    };

    await open('currency');

    expect(field('Currency name').value).toBe('server-default');
    expect(field('Starting balance').value).toBe('7');
    expect(field('Highest balance').value).toBe('');
  });

  it('shows the reason the server gives when it refuses the currency', async () => {
    refusal = { status: 'error', message: 'Decimal places must be between 0 and 8.' };

    await open('currency');
    await type(field('Decimal places'), '9');
    await click(buttonByText('Save currency'));

    expect(writes('PUT')[0]!.body).toMatchObject({ decimalPlaces: 9 });

    const alert = document.body.querySelector('[data-currency-form] [role="alert"]');

    expect(alert?.textContent).toContain('Decimal places must be between 0 and 8.');
  });

  it('leaves the limits to the server instead of a second copy in the form', async () => {
    await open('currency');

    expect(field('Decimal places').getAttribute('max')).toBeNull();
  });

  it('lists each earning rule by its source', async () => {
    await open('rules');

    expect(names('rule')).toEqual(['ChatMessage', 'Follow']);
    expect(rowOf('rule', 'Follow').textContent).toContain('Off');
  });

  it('turns a rule on by sending the whole rule with the flag flipped', async () => {
    await open('rules');
    await click(rowOf('rule', 'Follow').querySelector('button[role="switch"]') as HTMLElement);

    expect(writes('PUT')[0]!.body).toMatchObject({
      source: 'Follow',
      isEnabled: true,
      rate: 50,
    });

    expect(rowOf('rule', 'Follow').textContent).not.toContain('Off');
  });

  it('changes the rate of a rule from its edit dialog', async () => {
    await open('rules');
    await click(rowOf('rule', 'ChatMessage').querySelector('[data-rule-edit]') as HTMLElement);
    await type(field('Amount earned'), '8');
    await click(buttonByText('Save rule'));

    expect(writes('PUT')[0]!.body).toMatchObject({
      source: 'ChatMessage',
      rate: 8,
    });

    expect(rules.find((row) => row.id === 'r1')?.rate).toBe(8);
  });

  it('deletes a rule only after the confirm', async () => {
    await open('rules');
    await click(rowOf('rule', 'ChatMessage').querySelector('[data-rule-delete]') as HTMLElement);

    expect(writes('DELETE')).toHaveLength(0);

    await click(buttonByText('Delete rule'));

    expect(writes('DELETE').map((call) => call.path_?.ruleId)).toEqual(['r1']);
    expect(names('rule')).toEqual(['Follow']);
  });

  it('shows a rule another dashboard added once the live signal marks the list stale', async () => {
    await open('rules');

    rules = [...rules, rule('r3', 'Raid')];
    await queries.invalidateQueries({ queryKey: earningRulesKey('c1') });
    await settle();

    expect(names('rule')).toContain('Raid');
  });

  it('creates a savings jar with its goal and lists it', async () => {
    await open('jars');
    await click(buttonByText('New jar'));
    await type(field('Jar name'), 'Charity');
    await type(field('Goal'), '500');
    await click(buttonByText('Create jar'));

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'Charity',
      goalAmount: 500,
      isOpen: true,
    });

    expect(names('jar')).toContain('Charity');
  });

  it('sends the currency icon with the currency form', async () => {
    await open('currency');
    await type(field('Currency name'), 'Nomz');
    await type(field('Currency icon URL'), 'https://cdn.example/nomz.png');
    await click(buttonByText('Save currency'));

    expect(writes('PUT')[0]!.body).toMatchObject({ iconUrl: 'https://cdn.example/nomz.png' });
  });

  it('sends a jar icon and take-out cap with the new jar', async () => {
    await open('jars');
    await click(buttonByText('New jar'));
    await type(field('Jar name'), 'Charity');
    await type(field('Jar icon URL'), 'https://cdn.example/jar.png');
    await type(field('Take-out cap per channel'), '40');
    await click(buttonByText('Create jar'));

    expect(writes('POST')[0]!.body).toMatchObject({
      iconUrl: 'https://cdn.example/jar.png',
      maxWithdrawalPerChannel: 40,
    });
  });

  it('invites a channel into a jar with its caps', async () => {
    await open('jars');
    await click(rowOf('jar', 'New camera').querySelector('[data-jar-invite]') as HTMLElement);
    await type(field('Channel ID'), 'chan-2');
    await type(field('Put-in cap per stream'), '25');
    await type(field('Take-out cap'), '10');
    await click(buttonByText('Send invite'));

    expect(writes('POST')[0]!.path).toContain('/jars/{jarId}/invite');
    expect(writes('POST')[0]!.path_?.jarId).toBe('j1');

    expect(writes('POST')[0]!.body).toMatchObject({
      invitedBroadcasterId: 'chan-2',
      role: 'Partner',
      contributionCapPerStream: 25,
      withdrawalCap: 10,
    });
  });

  it('creates a store item with its limits and lists it', async () => {
    await open('catalog');
    await click(buttonByText('New item'));
    await type(field('Item name'), 'Song bump');
    await type(field('Effect kind'), 'pipeline');
    await type(field('Cost'), '300');
    await type(field('Stock'), '5');
    await type(field('Per viewer per stream'), '2');
    await type(field('Sort order'), '3');
    await type(field('Cooldown (seconds)'), '60');
    await type(field('Item icon URL'), 'https://cdn.example/i.png');
    await click(buttonByText('Save item'));

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'Song bump',
      sinkType: 'pipeline',
      cost: 300,
      stockLimit: 5,
      maxPerViewerPerStream: 2,
      sortOrder: 3,
      cooldownSeconds: 60,
      cooldownPerUser: false,
      iconUrl: 'https://cdn.example/i.png',
    });

    expect(names('catalog')).toContain('Song bump');
  });

  it('saves a leaderboard with its top places', async () => {
    await open('leaderboards');
    await click(buttonByText('New leaderboard'));
    await type(field('Places shown'), '5');
    await click(buttonByText('Save leaderboard'));

    expect(writes('PUT')[0]!.body).toMatchObject({
      metric: 'balance',
      scope: 'channel',
      period: 'alltime',
      topN: 5,
      isPublic: true,
    });
  });

  it('closes a jar from its switch', async () => {
    await open('jars');
    await click(rowOf('jar', 'New camera').querySelector('button[role="switch"]') as HTMLElement);

    expect(writes('PATCH')[0]!.path_?.jarId).toBe('j1');
    expect(writes('PATCH')[0]!.body).toEqual({ isOpen: false });
    expect(jars[0]!.isOpen).toBe(false);
  });

  it('deletes a jar only after the confirm', async () => {
    await open('jars');
    await click(rowOf('jar', 'New camera').querySelector('[data-jar-delete]') as HTMLElement);

    expect(writes('DELETE')).toHaveLength(0);

    await click(buttonByText('Delete jar'));

    expect(names('jar')).toEqual([]);
  });
});
