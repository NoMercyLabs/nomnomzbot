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
import { dashboardRoutes } from '@/router/dashboard';

import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import AdminOverviewView from '../AdminOverviewView.vue';
import { dashboardAdminOverviewRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const paths: string[] = [];
let stats: Record<string, unknown> = {};
let refused: boolean = false;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      paths.push(path);

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      return { data: { data: stats }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  paths.length = 0;
  refused = false;

  stats = {
    totalChannels: 12,
    activeChannels: 9,
    totalUsers: 1500,
    systemStatus: 'healthy',
    botUptimeSeconds: 93_784,
    eventsProcessedToday: 4321,
  };

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
  vi.restoreAllMocks();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminOverviewView, {
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

const text = (): string => document.body.textContent ?? '';
const tiles = (): string[] =>
  [...document.body.querySelectorAll('[data-slot="stat-tile"]')].map(
    (node) => node.textContent ?? '',
  );

describe('AdminOverviewView', () => {
  it('reads the platform numbers from admin/stats and shows one tile for each', async () => {
    await open();

    expect(paths).toEqual(['/api/v{version}/admin/stats']);
    expect(tiles()).toHaveLength(6);
    expect(tiles()[0]).toContain('Channels');
    expect(tiles()[0]).toContain('12');
    expect(tiles()[1]).toContain('9');
    expect(tiles()[2]).toContain('1,500');
    expect(tiles()[5]).toContain('4,321');
  });

  it('shows the uptime as days, hours and minutes', async () => {
    await open();

    expect(text()).toContain('1d 2h 3m');
  });

  it('marks a system that is not healthy as a warning', async () => {
    stats.systemStatus = 'degraded';
    await open();

    const status = document.body.querySelector('[data-slot="stat-tile"] b.text-destructive');

    expect(status?.textContent).toContain('Degraded');
  });

  it('shows the error state when the bot refuses the read', async () => {
    refused = true;
    await open();

    expect(text()).toContain('The platform numbers could not load.');
    expect(tiles()).toHaveLength(0);
  });

  it('carries the restricted-option marker for the hosted mode', async () => {
    await open();

    expect(text()).toContain('earn money with NomNomzBot');
  });

  it('is registered as the real overview page, not the coming-soon scaffold', () => {
    const children = dashboardRoutes.children ?? [];
    const found = children.filter((route) => route.name === 'dashboard-admin-overview');

    expect(found).toHaveLength(1);
    expect(found[0]!.path).toBe(dashboardAdminOverviewRoute.path);
    expect(dashboardAdminOverviewRoute.meta.navMode).toBe('admin');
  });

  it('has every key in the Dutch file that the English file has', () => {
    const keys = (value: unknown, prefix: string = ''): string[] =>
      Object.entries(value as Record<string, unknown>).flatMap(([key, inner]) =>
        typeof inner === 'object' && inner !== null
          ? keys(inner, `${prefix}${key}.`)
          : [`${prefix}${key}`],
      );

    expect(keys(nl).sort()).toEqual(keys(en).sort());
  });
});
