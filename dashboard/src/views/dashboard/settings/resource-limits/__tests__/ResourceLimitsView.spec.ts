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
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import { dashboardSettingsRoutes } from '../../route';

import en from '../i18n/en.json';
import ResourceLimitsView from '../ResourceLimitsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const gigabytes2 = 2_147_483_648;

const limits = [
  {
    limitKey: 'sound_clip_storage_bytes',
    class: 0,
    displayName: 'Sound clip storage',
    currentCount: 3_394_022,
    limit: gigabytes2,
    safetyBaseline: 0,
  },
  {
    limitKey: 'channel_asset_storage_bytes',
    class: 0,
    displayName: 'Channel asset storage',
    currentCount: '2000000000',
    limit: gigabytes2,
    safetyBaseline: 0,
  },
  {
    limitKey: 'sandbox_exec_ms',
    class: 0,
    displayName: 'Script CPU time per month',
    currentCount: 90_000,
    limit: 1_800_000,
    safetyBaseline: 0,
  },
  {
    limitKey: 'tts_max_characters',
    class: 0,
    displayName: 'TTS characters per month',
    currentCount: 0,
    limit: 500,
    safetyBaseline: 0,
  },
  {
    limitKey: 'custom_commands',
    class: 1,
    displayName: 'Custom commands',
    currentCount: 12,
    limit: 1500,
    safetyBaseline: 1500,
  },
];

const usage = [
  {
    metricKey: 'tts_characters',
    used: 250,
    limit: 1000,
    remaining: 750,
    periodStart: '2026-10-01T00:00:00Z',
    periodEnd: '2026-11-01T00:00:00Z',
  },
];

const entitlement = {
  tierKey: 'plus',
  allowsCustomBotName: true,
  prioritySupport: false,
  limits: {},
};

const tiersList: {
  key: string;
  displayName: string;
  sortOrder: number;
}[] = [
  {
    key: 'plus',
    displayName: 'Plus',
    sortOrder: 1,
  },
];

function answer(path: string): Answer {
  if (path.endsWith('/billing/limits'))
    return { data: { data: limits }, response: { ok: true, status: 200 } };

  if (path.endsWith('/billing/usage'))
    return { data: { data: usage }, response: { ok: true, status: 200 } };

  if (path.endsWith('/billing/tiers'))
    return { data: { data: tiersList }, response: { ok: true, status: 200 } };

  return { data: { data: entitlement }, response: { ok: true, status: 200 } };
}

let mounted: VueWrapper | null = null;

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', component: ResourceLimitsView }] });

  mounted = mount(ResourceLimitsView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const text = (): string => document.body.textContent ?? '';

function rowOf(label: string): HTMLElement {
  return [...document.body.querySelectorAll<HTMLElement>('[data-limit-row]')].find((row) =>
    row.textContent?.includes(label),
  )!;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  http.GET.mockReset();
  http.GET.mockImplementation(async (path: string) => answer(path));
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('Resource limits page', () => {
  it('reads limits, usage and plan of the current channel', async () => {
    await open();

    const paths = http.GET.mock.calls.map((call) => call[0]);

    expect(paths).toContain('/api/v{version}/channels/{channelId}/billing/limits');
    expect(paths).toContain('/api/v{version}/channels/{channelId}/billing/usage');
    expect(paths).toContain('/api/v{version}/channels/{channelId}/billing/entitlement');
    expect(http.GET.mock.calls[0]![1]).toMatchObject({ params: { path: { channelId: 'c1' } } });
  });

  it('shows storage in megabytes and gigabytes, never as raw bytes', async () => {
    await open();

    const storage = document.body.querySelector('[data-group="storage"]')!;

    expect(storage.textContent).toContain('Sound clip storage');
    expect(storage.textContent).toContain('3.2 MB of 2 GB');
    expect(storage.textContent).toContain('1.9 GB of 2 GB');
    expect(text()).not.toContain('2,147,483,648');
    expect(text()).not.toContain('3,394,022');
  });

  it('shows script time in minutes, never as milliseconds', async () => {
    await open();

    const allowance = document.body.querySelector('[data-group="allowance"]')!;

    expect(allowance.textContent).toContain('Script run time per month');
    expect(allowance.textContent).toContain('1.5 min of 30 min');
    expect(text()).not.toContain('1,800,000');
  });

  it('shows the text-to-speech cap as a per-message value with no usage bar', async () => {
    await open();

    const group = document.body.querySelector('[data-group="per-message"]')!;

    expect(group.textContent).toContain('500 characters per message');
    expect(group.querySelector('[role="progressbar"]')).toBeNull();
  });

  it('keeps abuse floors apart with their safety limit', async () => {
    await open();

    const floor = document.body.querySelector('[data-group="near-free"]')!;

    expect(floor.textContent).toContain('Custom commands');
    expect(floor.textContent).toContain('12 of 1,500');
    expect(floor.textContent).toContain('Safety limit for every channel: 1,500');
    expect(floor.textContent).not.toContain('Sound clip storage');
  });

  it('marks a near resource by level and draws a bar only for a limit', async () => {
    await open();

    const near = rowOf('Channel asset storage');

    expect(near.dataset.level).toBe('near');
    expect(near.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('93');
    expect(rowOf('Sound clip storage').dataset.level).toBe('ok');
  });

  it('draws no bar for an unlimited resource', async () => {
    limits[0]!.limit = -1;
    await open();

    const row = rowOf('Sound clip storage');

    expect(row.dataset.level).toBe('unlimited');
    expect(row.querySelector('[role="progressbar"]')).toBeNull();
    expect(row.textContent).toContain('3.2 MB · No limit');
    limits[0]!.limit = gigabytes2;
  });

  it('shows the plan and the restriction marker for hosted operation', async () => {
    await open();

    expect(document.body.querySelector('[data-plan="tier"]')?.textContent).toBe('Plus');
    expect(document.body.querySelector('[data-plan="custom-bot-name"]')?.textContent).toBe('Included');
    expect(document.body.querySelector('[data-plan="priority-support"]')?.textContent).toBe('Not included');
    expect(document.body.querySelector('[data-restricted]')?.textContent).toContain('earn money with NomNomzBot');
  });

  it('lists the metered usage of the period with its own word', async () => {
    await open();

    expect(text()).toContain('Text-to-speech characters');
    expect(text()).toContain('250 of 1,000');
    expect(text()).toContain('Oct 1, 2026 to Nov 1, 2026');
  });

  it('shows a limit once: the usage list leaves out metrics that repeat a limit, and the per-message cap', async () => {
    const extra = [
      {
        metricKey: 'event_responses',
        used: 0,
        limit: 400,
        remaining: 400,
        periodStart: '2026-10-01T00:00:00Z',
        periodEnd: '2026-11-01T00:00:00Z',
      },
      {
        metricKey: 'tts_max_characters',
        used: 0,
        limit: 8000,
        remaining: 8000,
        periodStart: '2026-10-01T00:00:00Z',
        periodEnd: '2026-11-01T00:00:00Z',
      },
      {
        metricKey: 'sound_clip_storage_bytes',
        used: 0,
        limit: gigabytes2,
        remaining: gigabytes2,
        periodStart: '2026-10-01T00:00:00Z',
        periodEnd: '2026-11-01T00:00:00Z',
      },
    ];

    usage.push(...extra);

    try {
      await open();

      const body = text();

      expect(body).toContain('Event responses');
      expect(body).not.toContain('event_responses');
      expect(body).not.toContain('tts_max_characters');
      expect(body).not.toContain('0 MB of 2 GB');
      expect(body.split('Sound clip storage').length - 1).toBe(1);
      expect(body).toContain('3.2 MB of 2 GB');
    } finally {
      usage.splice(usage.length - extra.length, extra.length);
    }
  });

  it('shows the monthly text-to-speech total as used out of the total, apart from the per-message cap', async () => {
    const monthly = {
      limitKey: 'tts_monthly_characters',
      class: 0,
      displayName: 'TTS characters per month',
      currentCount: 1200,
      limit: 25_000,
      safetyBaseline: 0,
    };

    limits.push(monthly);

    try {
      await open();

      const row = rowOf('TTS characters this month');

      expect(document.body.querySelector('[data-group="allowance"]')!.contains(row)).toBe(true);
      expect(row.textContent).toContain('1,200 of 25,000');
      expect(row.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('5');
      expect(document.body.querySelector('[data-group="per-message"]')!.textContent).toContain('Length of one TTS line');
      expect(document.body.querySelector('[data-group="per-message"]')!.textContent).not.toContain('this month');
    } finally {
      limits.splice(limits.indexOf(monthly), 1);
    }
  });

  it('shows No limit and no bar for a monthly text-to-speech total of -1', async () => {
    const monthly = {
      limitKey: 'tts_monthly_characters',
      class: 0,
      displayName: 'TTS characters per month',
      currentCount: 1200,
      limit: -1,
      safetyBaseline: 0,
    };

    limits.push(monthly);

    try {
      await open();

      const row = rowOf('TTS characters this month');

      expect(row.dataset.level).toBe('unlimited');
      expect(row.textContent).toContain('1,200 · No limit');
      expect(row.querySelector('[role="progressbar"]')).toBeNull();
    } finally {
      limits.splice(limits.indexOf(monthly), 1);
    }
  });

  it('says Not included, with no bar and no alarm, for a limit of 0', async () => {
    const original = limits[2]!.limit;

    limits[2]!.limit = 0;

    try {
      await open();

      const row = rowOf('Script run time per month');

      expect(row.dataset.level).toBe('excluded');
      expect(row.textContent).toContain('Not included');
      expect(row.textContent).not.toContain('0 of 0');
      expect(row.querySelector('[role="progressbar"]')).toBeNull();
    } finally {
      limits[2]!.limit = original;
    }
  });

  it('names the free plan by its display name, and never shows a plan key', async () => {
    const original = entitlement.tierKey;

    entitlement.tierKey = 'hosted_free';

    tiersList.push({
      key: 'hosted_free',
      displayName: 'Free',
      sortOrder: 0,
    });

    try {
      await open();
      expect(document.body.querySelector('[data-plan="tier"]')?.textContent).toBe('Free');

      tiersList.pop();
      mounted?.unmount();
      mounted = null;
      await open();

      expect(document.body.querySelector('[data-plan="tier"]')?.textContent).toBe('Your plan');
      expect(text()).not.toContain('hosted_free');
    } finally {
      entitlement.tierKey = original;
    }
  });

  it('puts the plan before the limits', async () => {
    await open();

    const plan = document.body.querySelector('[data-plan="tier"]')!;
    const storage = document.body.querySelector('[data-group="storage"]')!;

    expect(plan.compareDocumentPosition(storage) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});

describe('Resource limits route', () => {
  it('the sidebar entry resolves to the page and is not a coming-soon scaffold', () => {
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === 'dashboard-settings-resource-limits');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-resource-limits');

    expect(entry).toBeDefined();
    expect(record).toBeDefined();
    expect(record!.path).toBe('settings/resource-limits');
    expect(record!.meta).toMatchObject({ navMode: 'settings', action: 'billing:read' });
    expect(record!.meta?.comingSoon).toBeUndefined();
  });
});
