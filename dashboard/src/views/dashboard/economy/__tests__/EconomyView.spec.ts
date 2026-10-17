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
let config: Row | null = null;
let rules: Row[] = [];
let jars: Row[] = [];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const rule = (id: string, source: string, overrides: Row = {}): Row => ({
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
});

const jar = (id: string, name: string, overrides: Row = {}): Row => ({
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
});

function read(path: string): Answer {
  if (path.endsWith('/economy/config'))
    return ok(config);

  if (path.endsWith('/earning-rules'))
    return ok(rules);

  if (path.endsWith('/jars'))
    return ok(jars);

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

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  config = null;
  rules = [rule('r1', 'ChatMessage'), rule('r2', 'Follow', { isEnabled: false, rate: 50 })];
  jars = [jar('j1', 'New camera')];
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
