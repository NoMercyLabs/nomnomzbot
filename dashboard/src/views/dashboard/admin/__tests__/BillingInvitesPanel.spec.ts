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

import BillingInvitesPanel from '../components/BillingInvitesPanel.vue';
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

const invites = [{
  id: 'v1',
  code: 'WELCOME',
  maxRedemptions: 5,
  redemptionCount: 2,
  grantsFoundersBadge: false,
  grantsTierId: 't1',
  grantsTierKey: 'pro',
  grantsMonths: 6,
  expiresAt: '2027-06-01T10:00:00Z',
}, {
  id: 'v2',
  code: 'FRIENDS',
  maxRedemptions: 1,
  redemptionCount: 0,
  grantsFoundersBadge: false,
  grantsTierId: 't2',
  grantsTierKey: 'friends',
  grantsMonths: null,
  expiresAt: null,
}];

const created = {
  id: 'v9',
  code: 'NEWCODE42',
  maxRedemptions: 10,
  redemptionCount: 0,
  grantsFoundersBadge: false,
  grantsTierId: 't2',
  grantsTierKey: 'friends',
  grantsMonths: 3,
  expiresAt: null,
};

const reads = {
  '/invites': invites,
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

  const data = path.endsWith('/admin/billing/invites')
    ? created
    : {};

  return { data: { data }, response: { status: 200, ok: true } };
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
const writeText = vi.fn<(text: string) => Promise<void>>(async () => undefined);

beforeEach(() => {
  calls.length = 0;
  writeText.mockClear();
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
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
  await new Promise((done) => setTimeout(done, 50));
  await flushPromises();
};

const hook = (selector: string): HTMLElement => document.body.querySelector<HTMLElement>(selector)!;
const rowText = (id: string): string => hook(`[data-invite-row="${id}"]`).textContent ?? '';

function type(selector: string, value: string): void {
  const field = hook(selector) as HTMLInputElement;

  field.value = value;
  field.dispatchEvent(new Event('input'));
}

async function open(): Promise<void> {
  mounted = mount(BillingInvitesPanel, {
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

async function openForm(tier: string): Promise<void> {
  hook('[data-invite-new]').click();
  await settle();
  await pickTier(tier);
}

async function submit(): Promise<void> {
  hook('[data-form-action]').click();
  await settle();
}

// The one create call; a run that sent none, or more than one, throws instead of passing.
function createdBody(): unknown {
  const posts = calls.filter((call) => call.path.endsWith('/admin/billing/invites'));

  if (posts.length !== 1)
    throw new Error(`expected one create call, saw ${posts.length}`);

  return posts[0]!.body;
}

describe('BillingInvitesPanel', () => {
  it('creates an invite for a hidden tier for N months, with a cap and an expiry, and names it first', async () => {
    await open();
    await openForm('Friends');
    type('[data-length-months]', '3');
    type('[data-invite-max]', '10');
    type('[data-invite-expires]', '2026-12-01T12:00');
    await settle();

    expect(hook('[data-invite-summary]').textContent?.trim()).toBe('Gives Friends for 3 months');

    await submit();

    expect(createdBody()).toEqual({
      maxRedemptions: 10,
      grantsFoundersBadge: false,
      grantsTierId: 't2',
      grantsMonths: 3,
      expiresAt: '2026-12-01T12:00:00.000Z',
    });
  });

  it('creates a forever invite with no months and no expiry in the body', async () => {
    await open();
    await openForm('Pro');
    hook('[data-length-forever]').click();
    await settle();

    expect(hook('[data-invite-summary]').textContent?.trim()).toBe('Gives Pro forever');
    expect((hook('[data-length-months]') as HTMLInputElement).disabled).toBe(true);

    await submit();

    expect(createdBody()).toEqual({
      maxRedemptions: 1,
      grantsFoundersBadge: false,
      grantsTierId: 't1',
      grantsMonths: null,
      expiresAt: null,
    });
  });

  it('holds Create while the length is out of range and sends nothing', async () => {
    await open();
    await openForm('Pro');
    type('[data-length-months]', '121');
    await settle();

    expect((hook('[data-form-action]') as HTMLButtonElement).disabled).toBe(true);
    expect(document.body.querySelector('[data-invite-summary]')).toBeNull();

    await submit();

    expect(calls).toHaveLength(0);
  });

  it('shows the new invite link right after creating and copies it', async () => {
    await open();
    await openForm('Friends');
    await submit();

    const link = `${window.location.origin}/invite/NEWCODE42`;

    expect(hook('[data-invite-created-link]').textContent?.trim()).toBe(link);

    hook('[data-invite-created] [data-copy-link]').click();
    await settle();

    expect(writeText).toHaveBeenCalledWith(link);
  });

  it('lists each invite with its tier, its length, the uses left and the expiry', async () => {
    await open();

    expect(rowText('v1')).toContain('Pro');
    expect(rowText('v1')).toContain('6 months');
    expect(rowText('v1')).toContain('2 of 5 used');
    expect(rowText('v1')).toContain('3 left');
    expect(rowText('v1')).toContain('Expires Jun 1, 2027');
    expect(rowText('v1')).not.toContain('Forever');
    expect(rowText('v2')).toContain('Friends');
    expect(rowText('v2')).toContain('Forever');
    expect(rowText('v2')).toContain('0 of 1 used');
    expect(rowText('v2')).toContain('1 left');
    expect(rowText('v2')).not.toContain('Expires');
  });

  it('copies the share link of one invite row', async () => {
    await open();

    hook('[data-invite-row="v1"] [data-copy-link]').click();
    await settle();

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/invite/WELCOME`);
  });

  it('revokes one invite by its id, and only after the confirm', async () => {
    await open();

    hook('[data-invite-row="v2"] [data-invite-revoke]').click();
    await settle();

    expect(document.body.textContent).toContain('Revoke FRIENDS?');
    expect(calls).toHaveLength(0);

    hook('[data-confirm-action]').click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]!.method).toBe('POST');
    expect(calls[0]!.path).toBe('/api/v{version}/admin/billing/invites/{inviteCodeId}/revoke');
  });
});
