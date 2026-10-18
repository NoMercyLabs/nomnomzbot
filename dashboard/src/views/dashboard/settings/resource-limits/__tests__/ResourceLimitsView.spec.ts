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
import { settingsLaneCRoutes } from '../../laneCRoutes';

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

const limits = [
  {
    limitKey: 'tts.chars',
    class: 0,
    displayName: 'Stored clips',
    currentCount: 90,
    limit: 100,
    safetyBaseline: 0,
  },
  {
    limitKey: 'cmd.count',
    class: 1,
    displayName: 'Commands',
    currentCount: 12,
    limit: 500,
    safetyBaseline: 500,
  },
  {
    limitKey: 'asset.bytes',
    class: 0,
    displayName: 'Stored files',
    currentCount: '4000',
    limit: -1,
    safetyBaseline: 0,
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

function answer(path: string): Answer {
  if (path.endsWith('/billing/limits'))
    return { data: { data: limits }, response: { ok: true, status: 200 } };

  if (path.endsWith('/billing/usage'))
    return { data: { data: usage }, response: { ok: true, status: 200 } };

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

  it('splits cost-driving resources from abuse floors and shows used of limit', async () => {
    await open();

    const cost = document.body.querySelector('[data-group="cost-driving"]')!;
    const floor = document.body.querySelector('[data-group="near-free"]')!;

    expect(cost.textContent).toContain('Stored clips');
    expect(cost.textContent).toContain('90 of 100');
    expect(cost.textContent).not.toContain('Commands');
    expect(floor.textContent).toContain('Commands');
    expect(floor.textContent).toContain('12 of 500');
    expect(floor.textContent).toContain('Safety limit for every channel: 500');
  });

  it('marks a near and an unlimited resource by level and draws a bar only for a limit', async () => {
    await open();

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-limit-row]')];
    const near = rows.find((row) => row.textContent?.includes('Stored clips'))!;
    const open_ = rows.find((row) => row.textContent?.includes('Stored files'))!;

    expect(near.dataset.level).toBe('near');
    expect(near.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('90');
    expect(open_.dataset.level).toBe('unlimited');
    expect(open_.querySelector('[role="progressbar"]')).toBeNull();
    expect(open_.textContent).toContain('no limit');
  });

  it('shows the plan and the restriction marker for hosted operation', async () => {
    await open();

    expect(document.body.querySelector('[data-plan="tier"]')?.textContent).toBe('plus');
    expect(document.body.querySelector('[data-plan="custom-bot-name"]')?.textContent).toBe('Included');
    expect(document.body.querySelector('[data-plan="priority-support"]')?.textContent).toBe('Not included');
    expect(document.body.querySelector('[data-restricted]')?.textContent).toContain('restricted option');
  });

  it('lists the metered usage of the period with its own word', async () => {
    await open();

    expect(text()).toContain('Text-to-speech characters');
    expect(text()).toContain('250 of 1,000');
    expect(text()).toContain('Oct 1, 2026 to Nov 1, 2026');
  });
});

describe('Resource limits route', () => {
  it('the sidebar entry resolves to the page and is not a coming-soon scaffold', () => {
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === 'dashboard-settings-resource-limits');
    const record = settingsLaneCRoutes.find((route) => route.name === 'dashboard-settings-resource-limits');

    expect(entry).toBeDefined();
    expect(record).toBeDefined();
    expect(record!.path).toBe('settings/resource-limits');
    expect(record!.meta).toMatchObject({ navMode: 'settings' });
    expect(record!.meta?.comingSoon).toBeUndefined();
  });
});
