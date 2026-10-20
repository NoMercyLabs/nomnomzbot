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

import AdminEventSubHealthView from '../AdminEventSubHealthView.vue';
import en from '../i18n/en.json';
import { dashboardAdminEventSubHealthRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };

const calls: Call[] = [];
let tenants: Record<string, unknown>[] = [];
let total: number = 0;
let refused: boolean = false;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({ path, query: { ...options?.params?.query } });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      return {
        data: {
          data: tenants,
          total,
          hasMore: total > 25,
          nextPage: 2,
        },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  refused = false;
  total = 2;

  tenants = [
    {
      broadcasterId: 'b1',
      channelDisplayName: 'Stoney_Eagle',
      topics: [
        {
          subscriptionId: 's1',
          eventType: 'channel.follow',
          version: '2',
          status: 'enabled',
          enabled: true,
          lastError: null,
          lastConfirmedAt: '2026-10-01T10:00:00Z',
        },
        {
          subscriptionId: 's2',
          eventType: 'channel.raid',
          version: '1',
          status: 'authorization_revoked',
          enabled: false,
          lastError: 'token gone',
          lastConfirmedAt: '2026-09-01T10:00:00Z',
        },
      ],
    },
    {
      broadcasterId: 'b2',
      channelDisplayName: 'Quiet_One',
      topics: [],
    },
  ];

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
  mounted = mount(AdminEventSubHealthView, {
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
const topicRows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-topic-row]'),
];
const channelRows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-channel-row]'),
];
const byToggle = (index: number): HTMLElement | undefined =>
  document.body.querySelectorAll<HTMLElement>('[data-details-toggle]')[index];
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

describe('AdminEventSubHealthView', () => {
  it('shows one row per channel with a plain verdict and no topic detail', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/eventsub/health');
    expect(channelRows()).toHaveLength(2);
    expect(channelRows()[0]!.textContent).toContain('Stoney_Eagle');
    expect(channelRows()[0]!.textContent).toContain('1 of 2 need attention');
    expect(channelRows()[0]!.textContent).toContain('Re-authorize the channel');
    expect(channelRows()[0]!.textContent).not.toContain('token gone');
    expect(channelRows()[0]!.querySelector('[data-tone="bad"]')).not.toBeNull();
    expect(channelRows()[1]!.textContent).toContain('Quiet_One');
    expect(channelRows()[1]!.textContent).toContain('No Twitch subscriptions');
    expect(topicRows()).toHaveLength(0);
    expect(text()).not.toContain('channel.follow');
    expect(text()).not.toContain('Version');
  });

  it('says a channel with only working subscriptions is working', async () => {
    tenants = [
      {
        broadcasterId: 'b3',
        channelDisplayName: 'Fine_One',
        topics: [
          {
            subscriptionId: 's9',
            eventType: 'channel.follow',
            version: '2',
            status: 'enabled',
            enabled: true,
            lastConfirmedAt: '2026-10-01T10:00:00Z',
          },
        ],
      },
    ];

    await open();

    expect(channelRows()[0]!.textContent).toContain('Working');
    expect(channelRows()[0]!.textContent).toContain('1 subscription');
    expect(channelRows()[0]!.querySelector('[data-tone="ok"]')).not.toBeNull();
  });

  it('shows every topic with state, version, error and date behind the details toggle', async () => {
    await open();
    byToggle(0)!.click();
    await settle();

    expect(topicRows()).toHaveLength(2);
    expect(topicRows()[0]!.textContent).toContain('Channel raid');
    expect(topicRows()[0]!.textContent).toContain('Authorization revoked');
    expect(topicRows()[0]!.textContent).toContain('token gone');
    expect(topicRows()[1]!.textContent).toContain('Channel follow');
    expect(topicRows()[1]!.textContent).toContain('Oct 1, 2026');
  });

  it('shows no raw topic key, version, mono text or empty-error word in the details', async () => {
    await open();
    byToggle(0)!.click();
    await settle();

    expect(text()).not.toContain('channel.follow');
    expect(text()).not.toContain('authorization_revoked');
    expect(text()).not.toContain('Version');
    expect(text()).not.toContain('None');
    expect(document.body.querySelector('[data-topic-row] .font-mono')).toBeNull();
  });

  it('pages the registry', async () => {
    total = 60;
    await open();
    byText('button', 'Next')!.click();
    await settle();

    expect(calls[calls.length - 1]!.query.Page).toBe(2);
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see the EventSub health.');
    expect(topicRows()).toHaveLength(0);
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-eventsub-health');

    expect(registered?.path).toBe('admin/eventsub-health');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminEventSubHealthRoute.beforeEnter);
  });
});
