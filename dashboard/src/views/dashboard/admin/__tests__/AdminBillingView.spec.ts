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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { keysOf } from '@/lib/array';

import AdminBillingView from '../AdminBillingView.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  body?: unknown;
};

const calls: Call[] = [];

const tier = {
  id: 't1',
  key: 'pro',
  displayName: 'Pro',
  priceCents: 900,
  currency: 'USD',
  allowsCustomBotName: true,
  prioritySupport: false,
  sortOrder: 2,
  limits: [{ limitKey: 'commands', limitValue: 50 }],
  isPublic: true,
};

const invoice = {
  id: 'i1',
  number: 'INV-1',
  status: 'paid',
  amountDueCents: 900,
  amountPaidCents: 900,
  currency: 'USD',
  issuedAt: '2026-10-01T10:00:00Z',
  dunningStatus: 'none',
  amountRefundedCents: 0,
};

const reads = {
  '/tiers': [tier],
  '/invites': [{
    id: 'v1',
    code: 'WELCOME',
    maxRedemptions: 5,
    redemptionCount: 2,
    grantsFoundersBadge: false,
  }],
  '/priced-units': [{
    id: 'u1',
    unitKey: 'tts',
    currency: 'USD',
    priceMinorUnitsPerBatch: 40,
    batchSize: 1000,
  }],
  '/invoices': [invoice],
  '/grants': [],
  '/preview': { affectedTenantCount: 7, sampleChannelNames: ['Quiet_One'] },
} as const satisfies Record<string, unknown>;

const answer = (path: string): unknown => {
  const suffix = keysOf(reads).find((key) => path.endsWith(key))!;

  return { data: { data: reads[suffix] }, response: { status: 200, ok: true } };
};

const write = async (method: string, path: string, options?: { body?: unknown }) => {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  return { data: { data: {} }, response: { status: 200, ok: true } };
};

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer(path),
    PUT: (path: string, options?: { body?: unknown }) => write('PUT', path, options),
    POST: (path: string, options?: { body?: unknown }) => write('POST', path, options),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 350));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminBillingView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const hook = (selector: string): HTMLElement => document.body.querySelector<HTMLElement>(selector)!;
const text = (): string => document.body.textContent ?? '';

describe('AdminBillingView', () => {
  it('lists tiers, invites and priced units from the server', async () => {
    await open();

    expect(text()).toContain('Pro');
    expect(text()).toContain('WELCOME');
    expect(text()).toContain('2 of 5 used');
    expect(text()).toContain('tts');
    expect(text()).toContain('restricted option');
  });

  it('counts the affected channels before it saves a tier, and sends that count back', async () => {
    await open();

    hook('[data-tier-edit]').click();
    await settle();
    hook('[data-tier-form] [data-form-action]').click();
    await settle();

    expect(text()).toContain('7 channels are on this tier');
    expect(calls.filter((call) => call.method === 'PUT')).toHaveLength(0);

    hook('[data-confirm-action]').click();
    await settle();

    const put = calls.find((call) => call.method === 'PUT')!;

    expect(put.path).toContain('/tiers/{tierId}');
    const sent = put.body as { confirmedAffectedTenantCount: number };

    expect(sent.confirmedAffectedTenantCount).toBe(7);
  });

  it('shows the amount before it refunds an invoice', async () => {
    await open();

    const field = hook('[data-channel-id]') as HTMLInputElement;

    field.value = 'c1';
    field.dispatchEvent(new Event('input'));
    await settle();
    hook('[data-invoice-refund]').click();
    await settle();

    expect(text()).toContain('Refund 9.00 USD?');
    expect(calls.filter((call) => call.path.includes('/refund'))).toHaveLength(0);

    hook('[data-confirm-action]').click();
    await settle();

    expect(calls.some((call) => call.path.includes('/invoices/{invoiceId}/refund'))).toBe(true);
  });
});
