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
import { dashboardSettingsRoutes } from '../../route';

import en from '../i18n/en.json';
import { trustFieldKeys } from '../trustFields';
import TrustPolicyView from '../TrustPolicyView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

const access = vi.hoisted(() => ({ manage: true }));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'trust:policy:manage' || access.manage }),
}));

const stored = {
  requestCountWeight: 0.25,
  accountAgeWeight: '0.25',
  contentAgeWeight: 0.25,
  contentPopularityWeight: 0.25,
  requestCountDecay: 2,
  accountAgeDecay: 3,
  contentAgeDecay: 4,
  contentPopularityDecay: 5,
  notFollowingFactor: 0.5,
  reputationBoostEnabled: true,
  affiliateBoost: 5,
  partnerBoost: 10,
  verifiedYouTubeBoost: 7,
  youTubeQualityPenaltyFactor: 0.8,
  skipPenalty: 1,
  timeoutPenalty: 2,
  banPenalty: 3,
  untrustedMax: 20,
  lowMax: 40,
  standardMax: 70,
  heatHalfLifeHours: 24,
  heatDeltaBan: 30,
  heatDeltaTimeout: 15,
  heatDeltaReportValidated: 10,
  heatDeltaAutoModDenied: 5,
  heatDeltaFilterHit: 2,
  isPinned: false,
};

const defaults = {
  ...stored,
  requestCountWeight: 0.4,
  accountAgeWeight: 0.2,
  contentAgeWeight: 0.2,
  contentPopularityWeight: 0.2,
  banPenalty: 9,
};

const ok = (value: unknown): Answer => ({
  data: { data: value },
  response: { ok: true, status: 200 },
});

let mounted: VueWrapper | null = null;

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: TrustPolicyView }],
  });

  mounted = mount(TrustPolicyView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const row = (field: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-field="${field}"]`)!;

const inputOf = (field: string): HTMLInputElement => row(field).querySelector<HTMLInputElement>('input')!;

const save = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-action="save"]')!;

async function type(field: string, value: string): Promise<void> {
  const input = inputOf(field);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

const getsOf = (suffix: string): number =>
  http.GET.mock.calls.filter((call) => call[0].endsWith(suffix)).length;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.manage = true;
  http.GET.mockReset();
  http.PUT.mockReset();

  http.GET.mockImplementation(async (path: string) =>
    path.endsWith('/trust/policy/defaults')
      ? ok(defaults)
      : ok(stored));

  http.PUT.mockImplementation(async (_path: string, init: unknown) =>
    ok((init as { body: object }).body));
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('Trust policy page', () => {
  it('shows all 26 fields with the stored value, numbers or text alike', async () => {
    await open();

    expect(trustFieldKeys).toHaveLength(26);

    const missing = trustFieldKeys.filter((key) => row(key) === null);

    expect(missing).toEqual([]);
    expect(inputOf('requestCountWeight').value).toBe('0.25');
    expect(inputOf('accountAgeWeight').value).toBe('0.25');
    expect(inputOf('standardMax').value).toBe('70');
    expect(row('reputationBoostEnabled').querySelector('[role="switch"]')?.getAttribute('aria-checked')).toBe('true');
  });

  it('names the default of each field from the defaults endpoint', async () => {
    await open();

    expect(getsOf('/trust/policy/defaults')).toBe(1);
    expect(row('requestCountWeight').querySelector('[data-row-hint]')?.textContent).toContain('0.4');
    expect(row('banPenalty').querySelector('[data-row-hint]')?.textContent).toContain('9');
  });

  it('keeps Save off until a field changes, then sends all fields and refetches the policy', async () => {
    await open();

    expect(save().disabled).toBe(true);

    await type('banPenalty', '12');

    expect(save().disabled).toBe(false);

    save().click();
    await flushPromises();

    const call = http.PUT.mock.calls[0]!;
    const init = call[1] as {
      params: { path: { channelId: string } };
      body: Record<string, unknown>;
    };

    expect(call[0]).toBe('/api/v{version}/channels/{channelId}/trust/policy');
    expect(init.params.path.channelId).toBe('c1');
    expect(init.body.banPenalty).toBe(12);
    expect(init.body.accountAgeWeight).toBe(0.25);
    expect(init.body.reputationBoostEnabled).toBe(true);
    expect(Object.keys(init.body)).toHaveLength(26);
    expect('isPinned' in init.body).toBe(false);
    expect(getsOf('/trust/policy')).toBe(2);
  });

  it('shows the live sum of the four score weights', async () => {
    await open();

    expect(document.body.querySelector('[data-weight-sum]')?.textContent).toContain('1');

    await type('requestCountWeight', '0.4');

    expect(document.body.querySelector('[data-weight-sum]')?.textContent).toContain('1.15');
  });

  it('puts the bot refusal on the row that caused it, in words', async () => {
    http.PUT.mockResolvedValue({
      error: {
        status: 'error',
        message: 'x',
        code: 'VALIDATION_FAILED',
        data: {
          key: 'trust.policy.error.weightsMustSumToOne',
          field: 'requestCountWeight',
        },
      },
      response: { ok: false, status: 400 },
    });

    await open();
    await type('requestCountWeight', '0.9');

    save().click();
    await flushPromises();

    const message = row('requestCountWeight').querySelector('[data-row-error]')?.textContent;

    expect(message).toBe('The four score weights must add up to exactly 1.');
    expect(row('banPenalty').querySelector('[data-row-error]')).toBeNull();
    expect(getsOf('/trust/policy')).toBe(1);
  });

  it('resets one field to its default without saving', async () => {
    await open();
    await type('banPenalty', '12');

    row('banPenalty').querySelector<HTMLButtonElement>('[data-action="reset-field"]')!.click();
    await flushPromises();

    expect(inputOf('banPenalty').value).toBe('9');
    expect(http.PUT).not.toHaveBeenCalled();
  });

  it('locks every field and offers no Save to a person without the manage right', async () => {
    access.manage = false;
    await open();

    expect(document.body.querySelector('fieldset[data-slot="read-only-scope"]')?.hasAttribute('disabled')).toBe(true);
    expect(save()).toBeNull();
  });
});

describe('Trust policy route', () => {
  it('the sidebar entry resolves to the page and is gated by the read right', () => {
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === 'dashboard-settings-trust-policy');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-trust-policy');

    expect(entry).toBeDefined();
    expect(record!.path).toBe('settings/trust-policy');

    expect(record!.meta).toMatchObject({
      navMode: 'settings',
      action: 'trust:policy:read',
    });
  });
});
