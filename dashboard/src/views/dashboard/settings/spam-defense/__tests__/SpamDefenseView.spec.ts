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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';
import adminEn from '@/views/dashboard/admin/i18n/en.json';

import SpamDefenseView from '../SpamDefenseView.vue';
import { settingsSpamDefenseRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: { body: Record<string, unknown> }) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

const access = vi.hoisted(() => ({ manage: true }));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'spam:policy:manage' || access.manage }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

const descriptor = (key: string, group: string, over: Record<string, unknown> = {}) =>
  ({
    key,
    group,
    labelKey: `spam_setting_${key}_label`,
    explanationKey: `spam_setting_${key}_explanation`,
    costKey: `spam_setting_${key}_cost`,
    isToggle: false,
    ...over,
  }) as const satisfies Record<string, unknown>;

const policy = {
  settings: {
    isEnabled: true,
    dryRun: false,
    accountAgeGateDays: 7,
    trustThresholds: {
      establishedDays: 30,
      establishedMessages: 100,
      establishedDistinctActiveDays: 10,
      establishedStrikeFreeDays: 14,
    },
  },
  // cspell:ignore catalogue (the bot's wire name for the list)
  catalogue: [
    descriptor('IsEnabled', 'master', { isToggle: true }),
    descriptor('DryRun', 'master', { isToggle: true }),
    descriptor('TrustThresholds', 'trust', { isToggle: true }),
    descriptor('AccountAgeGateDays', 'newcomers', { minimum: 0, maximum: 365 }),
  ],
  invariants: [],
  isPinned: true,
};

const ok = (value: unknown): Answer => ({
  data: { data: value },
  response: { ok: true, status: 200 },
});

let mounted: VueWrapper | null = null;

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(SpamDefenseView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const row = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-setting="${key}"]`)!;
const field = (key: string): HTMLInputElement => row(key).querySelector('input')!;
const save = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-save]')!;

async function type(key: string, value: string): Promise<void> {
  const input = field(key);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', adminEn);
  access.manage = true;
  http.GET.mockReset();
  http.PUT.mockReset();
  http.GET.mockResolvedValue(ok(policy));
  http.PUT.mockResolvedValue({ data: { status: 'ok' }, response: { ok: true, status: 200 } });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('SpamDefenseView', () => {
  it('reads the channel policy from the channel route and shows its values', async () => {
    await open();

    expect(http.GET.mock.calls[0]?.[0]).toBe(
      '/api/v{version}/channels/{channelId}/spam-defense/policy',
    );

    expect(field('accountAgeGateDays').value).toBe('7');
    expect(field('establishedDays').value).toBe('30');
    expect(save().disabled).toBe(true);
  });

  it('sends the whole record with the change to the channel route', async () => {
    await open();
    await type('accountAgeGateDays', '14');
    await type('establishedMessages', '150');
    expect(save().disabled).toBe(false);

    save().click();
    await flushPromises();

    expect(http.PUT).toHaveBeenCalledTimes(1);

    expect(http.PUT.mock.calls[0]?.[0]).toBe(
      '/api/v{version}/channels/{channelId}/spam-defense/policy',
    );

    expect(http.PUT.mock.calls[0]?.[1].body).toEqual({
      isEnabled: true,
      dryRun: false,
      accountAgeGateDays: 14,
      trustThresholds: {
        establishedDays: 30,
        establishedMessages: 150,
        establishedDistinctActiveDays: 10,
        establishedStrikeFreeDays: 14,
      },
    });
  });

  it('blocks Save and names the range for a value outside the catalogue limits', async () => {
    await open();
    await type('accountAgeGateDays', '999');

    expect(save().disabled).toBe(true);
    expect(row('accountAgeGateDays').textContent).toContain('0 to 365');
    expect(http.PUT).not.toHaveBeenCalled();
  });

  it('shows the translated line with its range next to each field the bot refused', async () => {
    http.PUT.mockResolvedValue({
      error: {
        data: {
          violations: [
            {
              key: 'spam.policy.error.outOfRange',
              field: 'accountAgeGateDays',
              minimum: 0,
              maximum: 365,
            },
            { key: 'spam.policy.error.maxWindowBelowWindow', field: 'establishedDays' },
          ],
        },
      },
      response: { ok: false, status: 400 },
    });

    await open();
    await type('accountAgeGateDays', '14');
    save().click();
    await flushPromises();

    const range = row('accountAgeGateDays').querySelector('[data-invalid]');

    expect(range?.textContent).toBe('Enter a value from 0 to 365.');

    expect(row('establishedDays').querySelector('[data-invalid]')?.textContent).toContain(
      'at least as long',
    );

    expect(row('isEnabled').querySelector('[data-invalid]')).toBeNull();
  });

  it('shows no Save and no switch control to a person without the manage key', async () => {
    access.manage = false;
    await open();

    expect(document.body.querySelector('[data-save]')).toBeNull();
    expect(row('isEnabled').querySelector('[role="switch"]')).toBeNull();
  });

  it('offers a jump button per section that scrolls its section into view', async () => {
    const scrolled: string[] = [];

    Element.prototype.scrollIntoView = function scrollIntoView(this: Element): void {
      scrolled.push(this.getAttribute('data-group') ?? '');
    };

    await open();

    const jumps = [...document.body.querySelectorAll<HTMLButtonElement>('[data-jump]')];

    expect(jumps.map((jump) => jump.dataset.jump)).toEqual(['master', 'trust', 'newcomers']);
    expect(jumps[2]?.textContent).toContain('New accounts and new followers');

    jumps[2]?.click();
    await flushPromises();

    expect(scrolled).toEqual(['newcomers']);
  });

  it('is a registered settings route gated by the read key', () => {
    expect(settingsSpamDefenseRoute.path).toBe('settings/spam-defense');
    expect(settingsSpamDefenseRoute.meta.action).toBe('spam:policy:read');
  });
});
