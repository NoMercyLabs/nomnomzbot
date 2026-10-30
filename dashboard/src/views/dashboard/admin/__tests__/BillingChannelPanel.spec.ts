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
import { keysOf } from '@/lib/array';

import BillingChannelPanel from '../components/BillingChannelPanel.vue';
import en from '../i18n/en.json';

process.env.TZ = 'UTC';

type Call = {
  method: string;
  path: string;
  body?: unknown;
};

const calls: Call[] = [];

const tiers = [{
  id: 't1',
  key: 'pro',
  displayName: 'Pro',
  priceCents: 900,
  currency: 'USD',
  allowsCustomBotName: true,
  prioritySupport: false,
  sortOrder: 2,
  limits: [],
  isPublic: true,
}, {
  id: 't2',
  key: 'friends',
  displayName: 'Friends',
  priceCents: 0,
  currency: 'USD',
  allowsCustomBotName: true,
  prioritySupport: false,
  sortOrder: 3,
  limits: [],
  isPublic: false,
}];

const grants = [{
  id: 'g-forever',
  broadcasterId: 'c1',
  grantedTierId: 't2',
  grantedTierKey: 'friends',
  reason: 'Old friend',
  expiresAt: null,
  issuedAt: '2026-10-01T10:00:00Z',
}, {
  id: 'g-dated',
  broadcasterId: 'c1',
  grantedTierId: 't1',
  grantedTierKey: 'pro',
  reason: 'Beta tester',
  expiresAt: '2027-03-01T10:00:00Z',
  issuedAt: '2026-10-01T10:00:00Z',
}];

// Ordered: the preview path also contains "/grants", so it is matched first.
const reads = {
  '/grants/preview': {
    currentTierKey: 'free',
    grantedTierKey: 'pro',
    changedLimitCount: 2,
    changedLimitKeys: ['commands', 'timers'],
  },
  '/grants': grants,
  '/invoices': [],
  '/tiers': tiers,
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
  vi.useRealTimers();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 350));
  await flushPromises();
};

const hook = (selector: string): HTMLElement => document.body.querySelector<HTMLElement>(selector)!;
const text = (): string => document.body.textContent ?? '';

function type(selector: string, value: string): void {
  const field = hook(selector) as HTMLInputElement;

  field.value = value;
  field.dispatchEvent(new Event('input'));
}

async function open(): Promise<void> {
  mounted = mount(BillingChannelPanel, {
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

  type('[data-channel-id]', 'c1');
  await settle();
}

async function pickTier(label: string): Promise<void> {
  // jsdom lacks these three; the select calls them when it opens.
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;

  hook('[data-length-tier]').dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await settle();

  const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
    (node) => node.textContent?.trim() === label,
  )!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
  await settle();
}

async function openGift(label: string): Promise<void> {
  hook('[data-grant-gift]').click();
  await settle();
  await pickTier(label);
  type('[data-gift-reason]', 'Thanks for testing');
  await settle();
}

async function issueGift(): Promise<void> {
  hook('[data-form-action]').click();
  await settle();
  hook('[data-confirm-action]').click();
  await settle();
}

const issued = (): Call[] =>
  calls.filter((call) => call.path.endsWith('/channels/{broadcasterId}/grants'));

describe('BillingChannelPanel gifts', () => {
  it('gifts a hidden tier for N months and names the end date before it is issued', async () => {
    await open();
    await openGift('Friends');
    // Only the clock is frozen, and only after the tier is picked: the select reads time while it opens.
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-29T12:00:00Z'));
    type('[data-length-months]', '3');
    await settle();

    expect(hook('[data-gift-summary]').textContent?.trim()).toBe('Gives Friends until Jan 29, 2027');

    hook('[data-form-action]').click();
    await settle();

    expect(text()).toContain('Gives Friends until Jan 29, 2027');
    expect(text()).toContain('2 limits change.');
    expect(issued()).toHaveLength(0);

    hook('[data-confirm-action]').click();
    await settle();

    expect(issued()).toHaveLength(1);

    expect(issued()[0]!.body).toEqual({
      tierId: 't2',
      reason: 'Thanks for testing',
      months: 3,
      forever: false,
      confirmedChangedLimitCount: 2,
    });
  });

  it('gifts a tier forever with no months and no end date in the body', async () => {
    await open();
    await openGift('Pro');
    hook('[data-length-forever]').click();
    await settle();

    expect(hook('[data-gift-summary]').textContent?.trim()).toBe('Gives Pro forever');
    expect((hook('[data-length-months]') as HTMLInputElement).disabled).toBe(true);

    await issueGift();

    expect(issued()).toHaveLength(1);

    expect(issued()[0]!.body).toEqual({
      tierId: 't1',
      reason: 'Thanks for testing',
      forever: true,
      confirmedChangedLimitCount: 2,
    });
  });

  it('lists each live gift with its tier name and its end, Forever for no end date', async () => {
    await open();

    const forever = hook('[data-grant-row="g-forever"]').textContent ?? '';
    const dated = hook('[data-grant-row="g-dated"]').textContent ?? '';

    expect(forever).toContain('Friends');
    expect(forever).toContain('Old friend');
    expect(forever).toContain('Forever');
    expect(dated).toContain('Pro');
    expect(dated).toContain('Until Mar 1, 2027');
    expect(dated).not.toContain('Forever');
  });

  it('ends one gift now by its own id, and only after the confirm', async () => {
    await open();

    hook('[data-grant-row="g-dated"] [data-grant-end]').click();
    await settle();

    expect(text()).toContain('End the Pro gift now?');
    expect(calls.filter((call) => call.path.endsWith('/end'))).toHaveLength(0);

    hook('[data-confirm-action]').click();
    await settle();

    const ended = calls.filter((call) => call.path.endsWith('/grants/{grantId}/end'));

    expect(ended).toHaveLength(1);
    expect(ended[0]!.method).toBe('POST');
    expect(ended[0]!.body).toBeUndefined();
  });
});
