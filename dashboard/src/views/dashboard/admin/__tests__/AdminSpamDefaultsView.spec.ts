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

import AdminSpamDefaultsView from '../AdminSpamDefaultsView.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import { dashboardAdminSpamDefaultsRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const puts: Record<string, unknown>[] = [];
let refused: boolean = false;

const descriptor = (
  key: string,
  group: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  key,
  group,
  labelKey: `spam_setting_${key}_label`,
  explanationKey: `spam_setting_${key}_explanation`,
  costKey: `spam_setting_${key}_cost`,
  isToggle: false,
  ...over,
});

const policy = {
  settings: {
    isEnabled: true,
    dryRun: false,
    accountAgeGateDays: 7,
    qualifyNoStandingShare: 0.8,
    dequalifyNoStandingShare: 0.5,
    lockdownMinutes: 30,
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
    descriptor('QualifyNoStandingShare', 'campaign', { minimum: 0.5, maximum: 1 }),
    descriptor('DequalifyNoStandingShare', 'campaign', { minimum: 0.3, maximum: 0.95 }),
    descriptor('LockdownMinutes', 'lockdown', {
      minimum: 1,
      maximum: 240,
      pendingSlice: 'S-SPAM-LOCKDOWN-WIRE',
    }),
  ],
  invariants: [{ decision: 'SD8', guaranteeKey: 'spam_invariant_sd8_guarantee' }],
  isPinned: false,
};

vi.mock('vue-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (): Promise<Answer> => {
      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      return { data: { status: 'ok', data: policy }, response: { status: 200, ok: true } };
    },
    PUT: async (_path: string, options: { body: Record<string, unknown> }): Promise<Answer> => {
      puts.push(options.body);

      return { data: { status: 'ok' }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  puts.length = 0;
  refused = false;
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
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminSpamDefaultsView, {
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

const row = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-setting="${key}"]`)!;
const field = (key: string): HTMLInputElement => row(key).querySelector('input')!;
const saveButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-save]')!;

async function type(key: string, value: string): Promise<void> {
  const input = field(key);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

describe('AdminSpamDefaultsView', () => {
  it('renders one control per catalog entry, grouped, with the saas restriction and the guarantees', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-group]')).toHaveLength(5);
    expect(field('accountAgeGateDays').value).toBe('7');
    expect(field('establishedDays').value).toBe('30');
    expect(row('isEnabled').querySelector('[role="switch"]')?.getAttribute('aria-checked')).toBe('true');
    expect(row('lockdownMinutes').querySelector('[data-inactive]')).not.toBeNull();
    expect(row('accountAgeGateDays').querySelector('[data-inactive]')).toBeNull();
    expect(document.body.querySelector('[data-restricted]')?.textContent).toContain('restricted option');
    expect(document.body.querySelector('[data-invariants]')?.textContent).toContain('never actioned automatically');
  });

  it('keeps Save off until something changed, then sends the whole record with the change', async () => {
    await open();
    expect(saveButton().disabled).toBe(true);

    await type('accountAgeGateDays', '14');
    await type('establishedDays', '45');
    row('dryRun').querySelector<HTMLElement>('[role="switch"]')!.click();
    await settle();
    expect(saveButton().disabled).toBe(false);

    saveButton().click();
    await settle();

    expect(puts).toHaveLength(1);

    expect(puts[0]).toMatchObject({
      isEnabled: true,
      dryRun: true,
      accountAgeGateDays: 14,
      qualifyNoStandingShare: 0.8,
      trustThresholds: { establishedDays: 45, establishedMessages: 100 },
    });
  });

  it('refuses a value outside the range the bot reported and names the range', async () => {
    await open();
    await type('accountAgeGateDays', '999');

    expect(saveButton().disabled).toBe(true);
    expect(row('accountAgeGateDays').textContent).toContain('0 to 365');
    expect(puts).toHaveLength(0);
  });

  it('refuses an exoneration share that is not below the campaign share', async () => {
    await open();
    await type('dequalifyNoStandingShare', '0.9');

    expect(saveButton().disabled).toBe(true);
    expect(row('dequalifyNoStandingShare').textContent).toContain('must stay below');
  });

  it('says so when the platform admin may not read the defaults', async () => {
    refused = true;
    await open();

    expect(document.body.textContent).toContain('You may not see the spam defaults.');
    expect(document.body.querySelectorAll('[data-group]')).toHaveLength(0);
  });

  it('is a registered admin route with copy in both locales', () => {
    expect(dashboardAdminSpamDefaultsRoute.name).toBe('dashboard-admin-spam-defaults');
    expect(dashboardAdminSpamDefaultsRoute.path).toBe('admin/spam-defaults');

    expect(Object.keys(nl.dashboard.admin.spamDefaults.settings).sort())
      .toEqual(Object.keys(en.dashboard.admin.spamDefaults.settings).sort());

    expect(Object.keys(nl.dashboard.admin.spamDefaults.groups).sort())
      .toEqual(Object.keys(en.dashboard.admin.spamDefaults.groups).sort());
  });
});
