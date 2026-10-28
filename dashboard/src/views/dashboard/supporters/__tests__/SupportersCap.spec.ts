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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import SupportersView from '../SupportersView.vue';

function event(id: string, type: string, username: string, data: object) {
  return {
    id,
    type,
    userId: `guid-${username}`,
    twitchUserId: `u-${username}`,
    color: '#FF4500',
    username,
    data: JSON.stringify(data),
    timestamp: '2026-10-15T12:00:00Z',
  } as const satisfies object;
}

const gifts = Array.from({ length: 25 }, (_, index) =>
  event(`g${index}`, 'channel.subscription.gift', `gifter${index}`, { tier: '1000', giftCount: 1 }),
);

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => key === 'dashboard:read',
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/components/layout/UserName.vue', () => ({
  default: { template: '<span />' },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: {
        data: [
          ...gifts,
          event('s1', 'channel.subscribe', 'plain', { tier: '1000' }),
          event('s2', 'channel.subscription.message', 'resub', {
            tier: '1000',
            cumulativeMonths: 3,
          }),
        ],
      },
      response: { status: 200, ok: true },
    }),
  },
}));

let mounted: VueWrapper | null = null;

async function mountView(): Promise<VueWrapper> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(SupportersView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('the subscriptions panel', () => {
  it('shows ten rows and reveals ten more per press, so the page stays short', async () => {
    const view = await mountView();
    const panel = view.findAll('[data-slot="panel-card"]')[0]!;

    expect(panel.findAll('[data-slot="summary-row-body"]')).toHaveLength(10);

    await panel.get('[data-slot="show-more"]').trigger('click');

    expect(panel.findAll('[data-slot="summary-row-body"]')).toHaveLength(20);

    await panel.get('[data-slot="show-more"]').trigger('click');

    expect(panel.findAll('[data-slot="summary-row-body"]')).toHaveLength(27);
    expect(panel.find('[data-slot="show-more"]').exists()).toBe(false);
  });

  it('marks a plain subscribe, a resub and a gift with three different kind icons', async () => {
    const view = await mountView();
    const panel = view.findAll('[data-slot="panel-card"]')[0]!;

    await panel.get('[data-slot="show-more"]').trigger('click');
    await panel.get('[data-slot="show-more"]').trigger('click');

    const marks = panel.findAll('[role="img"]').map((icon) => icon.attributes('aria-label'));

    expect(new Set(marks)).toEqual(
      new Set([
        en.dashboard.supporters.subs.giftMark,
        en.dashboard.supporters.subs.subscribeMark,
        en.dashboard.supporters.subs.resubscribeMark,
      ]),
    );

    const rows = panel.findAll('[data-slot="summary-row-body"]');

    for (const row of rows) {
      expect(row.find('[role="img"]').exists()).toBe(true);
    }
  });
});
