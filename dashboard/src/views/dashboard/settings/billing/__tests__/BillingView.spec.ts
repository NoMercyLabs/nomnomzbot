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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import { FakeHubConnection, fakeSignalR } from '@/realtime/__tests__/fakeSignalR';
import { dashboardSettingsRoutes } from '../../route';

import en from '../i18n/en.json';
import BillingView from '../BillingView.vue';
import { answerHeldKeys } from '../../__tests__/heldKeys';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

type Call = [string, { params?: { query?: unknown }; body?: unknown }];

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@microsoft/signalr', (importOriginal) =>
  fakeSignalR(importOriginal));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const base = '/api/v{version}/channels/{channelId}/billing';
const ok = { ok: true, status: 200 };

const plus = {
  id: 't2',
  key: 'plus',
  displayName: 'Plus',
  priceCents: 1250,
  currency: 'EUR',
  allowsCustomBotName: true,
  prioritySupport: false,
  sortOrder: 2,
  limits: [
    { limitKey: 'sound_clip_storage_bytes', limitValue: 104_857_600 },
    { limitKey: 'channel_asset_storage_bytes', limitValue: -1 },
    { limitKey: 'tts_max_characters', limitValue: 500 },
    { limitKey: 'custom_commands', limitValue: 500 },
  ],
  isPublic: true,
};

const pro = {
  ...plus,
  id: 't3',
  key: 'pro',
  limits: [{ limitKey: 'sound_clip_storage_bytes', limitValue: 1_073_741_824 }],
  displayName: 'Pro',
  priceCents: 2500,
  sortOrder: 3,
  prioritySupport: true,
};

const free = {
  ...plus,
  id: 't1',
  key: 'free',
  displayName: 'Free',
  priceCents: 0,
  sortOrder: 1,
  allowsCustomBotName: false,
  limits: [],
};

const subscriptionOf = (tierKey: string, name: string, extra: object = {}) => ({
  id: 's1',
  broadcasterId: 'c1',
  tierKey,
  tierDisplayName: name,
  status: 'active',
  cancelAtPeriodEnd: false,
  currentPeriodEnd: '2026-11-15T00:00:00Z',
  isInviteOnlyGrant: false,
  allowsCustomBotName: true,
  prioritySupport: false,
  ...extra,
}) as const satisfies object;

function invoice(id: string, number: string) {
  return {
    id,
    number,
    status: 'paid',
    amountDueCents: 1250,
    amountPaidCents: 1250,
    currency: 'EUR',
    issuedAt: '2026-10-01T00:00:00Z',
    paidAt: '2026-10-02T00:00:00Z',
    hostedInvoiceUrl: 'https://pay.example/i/1',
    dunningStatus: 'none',
    amountRefundedCents: 0,
  } as const satisfies object;
}

let subscription: object = subscriptionOf('free', 'Free');
let badge: object | null = null;

function answerGet(path: string, init: unknown) {
  if (path === `${base}/subscription`)
    return { data: { data: subscription }, response: ok } as const satisfies Answer;

  if (path === `${base}/tiers`)
    return { data: { data: [pro, free, plus] }, response: ok } as const satisfies Answer;

  if (path === `${base}/founders-badge`)
    return { data: { data: badge }, response: ok } as const satisfies Answer;

  const page = (init as { params: { query: { Page: number } } }).params.query.Page;

  return {
    data: {
      data: [invoice(`i${page}`, `INV-${page}`)],
      hasMore: page < 2,
      total: 2,
    },
    response: ok,
  } as const satisfies Answer;
}

function answerPost(path: string) {
  if (path === `${base}/checkout`)
    return { data: { data: { checkoutUrl: 'https://pay.example/checkout' } }, response: ok } as const satisfies Answer;

  if (path === `${base}/portal`)
    return { data: { data: { portalUrl: 'https://pay.example/portal' } }, response: ok } as const satisfies Answer;

  if (path === `${base}/invite/validate`) {
    return {
      data: {
        data: {
          isValid: true,
          code: 'FOUNDER',
          grantsFoundersBadge: true,
          grantsTierKey: 'plus',
          remainingRedemptions: 3,
        },
      },
      response: ok,
    } as const satisfies Answer;
  }

  if (path === `${base}/invite/redeem`) {
    return {
      data: {
        data: {
          grantedFoundersBadge: true,
          grantedTierKey: 'plus',
          foundersBadge: null,
        },
      },
      response: ok,
    } as const satisfies Answer;
  }

  return { data: {}, response: ok } as const satisfies Answer;
}

let mounted: VueWrapper | null = null;
let queries: QueryClient;
let held: string[] = [];

async function open(): Promise<void> {
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: BillingView }],
  });

  mounted = mount(BillingView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const text = (): string => document.body.textContent ?? '';
const find = (selector: string): HTMLElement => document.body.querySelector<HTMLElement>(selector)!;

async function click(selector: string): Promise<void> {
  find(selector).click();
  await flushPromises();
}

async function type(selector: string, value: string): Promise<void> {
  const input = find(selector) as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input'));
  await flushPromises();
}

function postedTo(path: string): Call[] {
  return http.POST.mock.calls.filter((call) => call[0] === path) as Call[];
}

function readsOf(path: string): number {
  return http.GET.mock.calls.filter((call) => call[0] === path).length;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  subscription = subscriptionOf('free', 'Free');
  badge = null;
  http.GET.mockReset();
  http.POST.mockReset();
  http.GET.mockImplementation(async (path: string, init: unknown) => answerGet(path, init));
  held = ['billing:read', 'billing:manage'];

  answerHeldKeys(http.GET, () => held);

  http.POST.mockImplementation(async (path: string) => answerPost(path));
  FakeHubConnection.instances = [];

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
  vi.unstubAllGlobals();
});

describe('Billing page: plan', () => {
  it('shows the plan, its state, its renewal date and the hosted-service restriction', async () => {
    await open();

    expect(find('[data-plan="tier"]').textContent).toBe('Free');
    expect(find('[data-plan="status"]').textContent).toContain('Active');
    expect(find('[data-plan="period"]').textContent).toBe('Nov 15, 2026');
    expect(find('[data-plan="custom-bot-name"]').textContent).toBe('Included');
    expect(find('[data-plan="priority-support"]').textContent).toBe('Not included');
    expect(find('[data-restricted]').textContent).toContain('restricted option');
  });

  it('a plan set to end shows its end date and Keep my plan resumes it', async () => {
    subscription = subscriptionOf('plus', 'Plus', { cancelAtPeriodEnd: true });
    await open();

    expect(find('[data-plan="pending-cancel"]')).not.toBeNull();
    expect(text()).toContain('Ends on');
    expect(document.body.querySelector('[data-action="cancel"]')).toBeNull();

    await click('[data-action="resume"]');

    expect(postedTo(`${base}/resume`)).toHaveLength(1);
  });

  it('a plan from an invite code offers no payment or cancel', async () => {
    subscription = subscriptionOf('plus', 'Plus', { isInviteOnlyGrant: true });
    await open();

    expect(find('[data-plan="invite-grant"]')).not.toBeNull();
    expect(document.body.querySelector('[data-action="payment"]')).toBeNull();
    expect(document.body.querySelector('[data-action="cancel"]')).toBeNull();
  });

  it('Manage payment opens the payment page the server names', async () => {
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', { ...window.location, assign });
    subscription = subscriptionOf('plus', 'Plus');
    await open();
    await click('[data-action="payment"]');

    expect(postedTo(`${base}/portal`)).toHaveLength(1);
    expect(assign).toHaveBeenCalledWith('https://pay.example/portal');
  });

  it('cancel at the end of the period sends atPeriodEnd true with the trimmed reason and refreshes the plan', async () => {
    subscription = subscriptionOf('plus', 'Plus');
    await open();

    const reads = readsOf(`${base}/subscription`);

    await click('[data-action="cancel"]');
    await type('[data-field="cancel-reason"]', '  too pricey ');
    await click('[data-confirm-action]');

    const sent = postedTo(`${base}/cancel`);

    expect(sent).toHaveLength(1);
    expect(sent[0]![1].body).toEqual({ atPeriodEnd: true, reason: 'too pricey' });
    expect(readsOf(`${base}/subscription`)).toBe(reads + 1);
  });

  it('cancel right away sends atPeriodEnd false and no reason when none is typed', async () => {
    subscription = subscriptionOf('plus', 'Plus');
    await open();
    await click('[data-action="cancel"]');
    await click('[data-field="cancel-now"] [role="switch"]');
    await click('[data-confirm-action]');

    expect(postedTo(`${base}/cancel`)[0]![1].body).toEqual({ atPeriodEnd: false, reason: null });
  });
});

describe('Billing page: plans on offer', () => {
  it('lists the plans in order with a localized price and marks the current one', async () => {
    subscription = subscriptionOf('plus', 'Plus');
    await open();

    const keys = [...document.body.querySelectorAll<HTMLElement>('[data-tier]')].map(
      (row) => row.dataset.tier,
    );

    expect(keys).toEqual(['free', 'plus', 'pro']);
    expect(find('[data-tier="plus"] [data-tier-price]').textContent).toBe('€12.50 per month');
    expect(find('[data-tier="free"] [data-tier-price]').textContent).toBe('Free');
    expect(find('[data-tier="plus"] [data-current-marker]').textContent).toContain('Current plan');
    expect(document.body.querySelector('[data-tier="plus"] [data-action="choose"]')).toBeNull();
  });

  it('compares the plans side by side with plain limit names and units, never a raw key', async () => {
    subscription = subscriptionOf('plus', 'Plus');
    await open();

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-limit-key]')];
    const text = (key: string, tier: string) =>
      find(`[data-limit-key="${key}"] [data-limit-tier="${tier}"]`).textContent?.trim();

    expect(rows.map((row) => row.dataset.limitKey)).toEqual([
      'sound_clip_storage_bytes',
      'channel_asset_storage_bytes',
      'tts_max_characters',
    ]);

    expect(find('[data-limit-key="sound_clip_storage_bytes"]').textContent).toContain('Sound clip storage');
    expect(text('sound_clip_storage_bytes', 'plus')).toBe('100 MB');
    expect(text('sound_clip_storage_bytes', 'pro')).toBe('1 GB');
    expect(text('sound_clip_storage_bytes', 'free')).toBe('Not included');
    expect(text('channel_asset_storage_bytes', 'plus')).toBe('No limit');
    expect(text('tts_max_characters', 'plus')).toBe('500 characters');
    expect(find('[data-panel="tiers"]').textContent).not.toMatch(/_bytes|custom_commands|[0-9]{8}/);
  });

  it('without a paid plan, choosing a plan starts checkout with that tier key and goes to the payment page', async () => {
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', { ...window.location, assign });
    await open();
    await click('[data-tier="pro"] [data-action="choose"]');

    const sent = postedTo(`${base}/checkout`);

    expect(sent).toHaveLength(1);
    expect(sent[0]![1].body).toEqual({ tierKey: 'pro' });
    expect(postedTo(`${base}/change-tier`)).toHaveLength(0);
    expect(assign).toHaveBeenCalledWith('https://pay.example/checkout');
  });

  it('with a paid plan, switching changes the tier at the end of the period unless that is switched off', async () => {
    subscription = subscriptionOf('plus', 'Plus');
    await open();
    await click('[data-tier="pro"] [data-action="choose"]');
    await click('[data-field="at-period-end"] [role="switch"]');
    await click('[data-tier="free"] [data-action="choose"]');

    const bodies = postedTo(`${base}/change-tier`).map((call) => call[1].body);

    expect(bodies).toEqual([
      { tierKey: 'pro', atPeriodEnd: true },
      { tierKey: 'free', atPeriodEnd: false },
    ]);

    expect(postedTo(`${base}/checkout`)).toHaveLength(0);
  });
});

describe('Billing page: invite code and founders badge', () => {
  it('checks a code without using it and shows what it unlocks', async () => {
    await open();
    await type('[data-field="invite-code"]', ' FOUNDER ');
    await click('[data-action="check-code"]');

    expect(postedTo(`${base}/invite/validate`)[0]![1].body).toEqual({ code: 'FOUNDER' });
    expect(postedTo(`${base}/invite/redeem`)).toHaveLength(0);
    expect(find('[data-result="check"]').textContent).toContain('Unlocks the plan plus.');
    expect(find('[data-result="check"]').textContent).toContain('Gives the founders badge.');
    expect(find('[data-result="check"]').textContent).toContain('3 uses left.');
  });

  it('redeems a code, shows the result, clears the field and refreshes the badge', async () => {
    await open();

    const reads = readsOf(`${base}/founders-badge`);

    await type('[data-field="invite-code"]', 'FOUNDER');
    await click('[data-action="redeem-code"]');

    expect(postedTo(`${base}/invite/redeem`)[0]![1].body).toEqual({ code: 'FOUNDER' });
    expect(find('[data-result="redeemed"]').textContent).toContain('Founders badge granted.');
    expect((find('[data-field="invite-code"]') as HTMLInputElement).value).toBe('');
    expect(readsOf(`${base}/founders-badge`)).toBe(reads + 1);
  });

  it('shows the founders badge with its date and code, or says there is none', async () => {
    await open();

    expect(find('[data-badge="state"]').textContent).toBe('This channel has no founders badge.');

    mounted?.unmount();
    document.body.innerHTML = '';

    badge = {
      id: 'b1',
      grantedAt: '2026-09-01T00:00:00Z',
      isActive: true,
      inviteCode: 'FOUNDER',
    };

    await open();

    expect(find('[data-badge="state"]').textContent).toBe('This channel has the founders badge.');
    expect(find('[data-badge="detail"]').textContent).toBe('Since Sep 1, 2026 · From code FOUNDER');
  });
});

describe('Billing page: invoices', () => {
  it('lists the invoices with their amount and pages forward and back', async () => {
    await open();

    expect(find('[data-invoice-amount]').textContent).toBe('€12.50');
    expect(text()).toContain('INV-1');

    await click('[data-action="next"]');

    const pages = http.GET.mock.calls
      .filter((call) => call[0] === `${base}/invoices`)
      .map((call) => (call as unknown as Call)[1].params?.query);

    expect(pages).toEqual([
      { Page: 1, PageSize: 10 },
      { Page: 2, PageSize: 10 },
    ]);

    expect(text()).toContain('INV-2');
    expect((find('[data-action="next"]') as HTMLButtonElement).disabled).toBe(true);

    await click('[data-action="previous"]');

    expect(text()).toContain('INV-1');
  });
});

describe('Billing page: live updates', () => {
  it('a ConfigChanged billing event refetches the plan, the plans, the badge and the invoices', async () => {
    const hub = await import('@/realtime/dashboardHub');

    await open();
    await hub.startDashboardHub(queries);
    await hub.watchChannel('c1');

    const paths = ['subscription', 'tiers', 'founders-badge', 'invoices'].map(
      (name) => `${base}/${name}`,
    );
    const before = paths.map(readsOf);

    FakeHubConnection.at(FakeHubConnection.instances.length - 1).receive('ConfigChanged', {
      broadcasterId: 'c1',
      domain: 'billing',
      entityId: null,
      action: 'updated',
    });

    await flushPromises();

    expect(paths.map(readsOf)).toEqual(before.map((count) => count + 1));
  });
});

describe('Billing page: without the manage key', () => {
  it('shows the plan, the offers and the code check but no button that changes anything', async () => {
    held = ['billing:read'];
    subscription = subscriptionOf('pro', 'Pro');
    await open();

    expect(text()).toContain('Pro');
    expect(document.body.querySelectorAll('[data-tier]').length).toBeGreaterThan(0);
    expect(document.body.querySelector('[data-action="choose"]')).toBeNull();
    expect(document.body.querySelector('[data-action="cancel"]')).toBeNull();
    expect(document.body.querySelector('[data-action="payment"]')).toBeNull();
    expect(document.body.querySelector('[data-action="redeem-code"]')).toBeNull();
    expect(document.body.querySelector('[data-action="check-code"]')).not.toBeNull();
  });

  it('offers the change buttons again with the manage key', async () => {
    subscription = subscriptionOf('pro', 'Pro');
    await open();

    expect(document.body.querySelector('[data-action="choose"]')).not.toBeNull();
    expect(document.body.querySelector('[data-action="cancel"]')).not.toBeNull();
    expect(document.body.querySelector('[data-action="redeem-code"]')).not.toBeNull();
  });
});

describe('Billing route', () => {
  it('the sidebar entry resolves to the page and is not a coming-soon scaffold', () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-billing');
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-billing',
    );

    expect(entry).toBeDefined();
    expect(record).toBeDefined();
    expect(record!.path).toBe('settings/billing');
    expect(record!.meta).toMatchObject({ navMode: 'settings', action: 'billing:read' });
    expect(record!.meta?.comingSoon).toBeUndefined();
  });
});
