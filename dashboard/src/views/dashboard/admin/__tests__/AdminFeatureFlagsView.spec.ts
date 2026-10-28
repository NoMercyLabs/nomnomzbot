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

import AdminFeatureFlagsView from '../AdminFeatureFlagsView.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import { dashboardAdminFeatureFlagsRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type Call = {
  path: string;
  body?: Record<string, unknown>;
  params?: Record<string, unknown>;
};

const puts: Call[] = [];
const deletes: Call[] = [];
const reads: string[] = [];
let refused: boolean = false;

const flags = [
  {
    key: 'beta-widgets',
    description: 'New widget runtime',
    isEnabledGlobally: false,
    rolloutPercentage: 25,
    minTierKey: 'pro',
    requiresConsent: null,
    deploymentMode: 'saas',
  },
  {
    key: 'dark-launch',
    description: null,
    isEnabledGlobally: true,
    rolloutPercentage: 100,
    minTierKey: null,
    requiresConsent: null,
    deploymentMode: null,
  },
];

const overrides = [
  {
    flagKey: 'beta-widgets',
    broadcasterId: '01J0000000000000000000000A',
    channelName: 'kitte',
    isEnabled: true,
    reason: 'beta tester',
    expiresAt: null,
    updatedAt: '2026-10-17T10:00:00Z',
  },
];

const blast = { tenantsAffected: 12, sampleChannelNames: ['kitte', 'geennoob'] };

// The signed-in admin holds every platform key unless a case takes them away.
const keys = vi.hoisted(() => ({ held: true }));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => keys.held }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      reads.push(path);

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      if (path.endsWith('/blast-radius'))
        return { data: { status: 'ok', data: blast }, response: { status: 200, ok: true } };

      if (path.endsWith('/overrides'))
        return { data: { status: 'ok', data: overrides }, response: { status: 200, ok: true } };

      return { data: { status: 'ok', data: flags }, response: { status: 200, ok: true } };
    },
    PUT: async (path: string, options: Call): Promise<Answer> => {
      puts.push({
        path,
        body: options.body,
        params: options.params,
      });

      return { data: { status: 'ok' }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options: Call): Promise<Answer> => {
      deletes.push({ path, params: options.params });

      return { data: { status: 'ok' }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  keys.held = true;
  puts.length = 0;
  deletes.length = 0;
  reads.length = 0;
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
  mounted = mount(AdminFeatureFlagsView, {
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

const one = <T extends HTMLElement>(selector: string): T =>
  document.body.querySelector<T>(selector)!;

const field = (name: string): HTMLInputElement => one<HTMLInputElement>(`[data-field="${name}"]`);

async function type(selector: string, value: string): Promise<void> {
  const input = one<HTMLInputElement>(selector);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(selector: string): Promise<void> {
  one(selector).click();
  await settle();
}

describe('AdminFeatureFlagsView', () => {
  it('lists every flag with its state and every override with its channel, plus the saas restriction', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-flag]')).toHaveLength(2);
    expect(one('[data-flag="beta-widgets"]').textContent).toContain('25%');
    expect(one('[data-flag="beta-widgets"]').textContent).toContain('New widget runtime');
    expect(document.body.querySelectorAll('[data-override]')).toHaveLength(1);
    expect(one('[data-override]').textContent).toContain('kitte');
    expect(one('[data-override]').textContent).toContain('beta tester');
    expect(one('[data-restricted]').textContent).toContain('earn money with NomNomzBot');
  });

  it('shows the flags without new, edit or override actions when the admin lacks featureflag:write', async () => {
    keys.held = false;
    await open();

    expect(document.body.querySelectorAll('[data-override]')).toHaveLength(1);
    expect(document.body.querySelector('[data-new]')).toBeNull();
    expect(document.body.querySelector('[data-edit]')).toBeNull();
    expect(document.body.querySelector('[data-override-delete]')).toBeNull();
    expect(document.body.querySelector('[data-override-save]')).toBeNull();
  });

  it('shows the counted blast radius before it saves, then sends every field of the definition', async () => {
    await open();
    await click('[data-flag="beta-widgets"] [data-edit]');

    expect(field('description').value).toBe('New widget runtime');
    expect(field('rolloutPercentage').value).toBe('25');
    expect(field('minTierKey').value).toBe('pro');
    expect(field('deploymentMode').value).toBe('saas');

    await type('[data-field="rolloutPercentage"]', '50');
    await type('[data-field="requiresConsent"]', 'tracking');
    one('[data-field="isEnabledGlobally"]').click();
    await settle();

    await click('[data-review]');

    expect(puts).toHaveLength(0);
    expect(reads.some((path) => path.endsWith('/{flagKey}/blast-radius'))).toBe(true);
    expect(one('[data-blast]').textContent).toContain('12');
    expect(one('[data-blast]').textContent).toContain('geennoob');

    await click('[data-confirm]');

    expect(puts).toHaveLength(1);

    expect(puts[0]?.body).toEqual({
      key: 'beta-widgets',
      description: 'New widget runtime',
      isEnabledGlobally: true,
      rolloutPercentage: 50,
      minTierKey: 'pro',
      requiresConsent: 'tracking',
      deploymentMode: 'saas',
    });
  });

  it('refuses a rollout outside 0 to 100 and a flag with no key', async () => {
    await open();
    await click('[data-new]');

    expect(one<HTMLButtonElement>('[data-review]').disabled).toBe(true);

    await type('[data-field="key"]', 'fresh-flag');
    expect(one<HTMLButtonElement>('[data-review]').disabled).toBe(false);

    await type('[data-field="rolloutPercentage"]', '150');
    expect(one<HTMLButtonElement>('[data-review]').disabled).toBe(true);
    expect(one('[data-editor]').textContent).toContain('0 to 100');
  });

  it('saves a new flag without a preview because no channel can be reached yet', async () => {
    await open();
    await click('[data-new]');
    await type('[data-field="key"]', 'fresh-flag');
    await click('[data-review]');

    expect(puts).toHaveLength(1);

    expect(puts[0]?.body).toMatchObject({
      key: 'fresh-flag',
      isEnabledGlobally: false,
      rolloutPercentage: 100,
    });

    expect(document.body.querySelector('[data-blast]')).toBeNull();
  });

  it('sets a per-channel override for the chosen flag with its reason and expiry', async () => {
    await open();
    await click('[data-flag="dark-launch"] [data-edit]');
    await type('[data-field="overrideChannel"]', '01J0000000000000000000000B');
    await type('[data-field="overrideReason"]', 'kill switch');
    one('[data-field="overrideEnabled"]').click();
    await settle();
    await click('[data-override-save]');

    expect(puts).toHaveLength(1);

    expect(puts[0]?.params).toMatchObject({
      path: { flagKey: 'dark-launch', broadcasterId: '01J0000000000000000000000B' },
    });

    expect(puts[0]?.body).toMatchObject({ isEnabled: true, reason: 'kill switch' });
  });

  it('removes an override and reads the list again', async () => {
    await open();

    const before = reads.filter((path) => path.endsWith('/overrides')).length;

    await click('[data-override] [data-override-delete]');

    expect(deletes).toHaveLength(1);

    expect(deletes[0]?.params).toMatchObject({
      path: { flagKey: 'beta-widgets', broadcasterId: '01J0000000000000000000000A' },
    });

    expect(reads.filter((path) => path.endsWith('/overrides')).length).toBeGreaterThan(before);
  });

  it('says so when the platform admin may not read the flags', async () => {
    refused = true;
    await open();

    expect(document.body.textContent).toContain('You may not see the feature flags.');
    expect(document.body.querySelectorAll('[data-flag]')).toHaveLength(0);
  });

  it('is a registered admin route with copy in both locales', () => {
    expect(dashboardAdminFeatureFlagsRoute.name).toBe('dashboard-admin-feature-flags');
    expect(dashboardAdminFeatureFlagsRoute.path).toBe('admin/feature-flags');

    expect(Object.keys(nl.dashboard.admin.featureFlags).sort()).toEqual(
      Object.keys(en.dashboard.admin.featureFlags).sort(),
    );
  });
});
