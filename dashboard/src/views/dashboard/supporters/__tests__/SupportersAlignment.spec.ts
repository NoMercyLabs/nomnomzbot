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
          event('1', 'channel.subscribe', 'plain', { tier: '1000' }),
          event('2', 'channel.subscribe', 'gifted', { tier: '1000', isGift: true }),
        ],
      },
      response: { status: 200, ok: true },
    }),
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('the sub list rows', () => {
  it('reserve the same leading slot with and without a gift icon, so the text columns line up', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

    mounted = mount(SupportersView, {
      attachTo: document.body,
      global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
    });

    await flushPromises();

    const rows = mounted.findAll('[data-slot="summary-row-body"]');
    const leading = rows.map((row) => row.element.firstElementChild);

    expect(rows.length).toBeGreaterThanOrEqual(2);

    for (const first of leading) {
      expect(first?.getAttribute('data-slot')).not.toBe('summary-row-text');
      expect(first?.classList.contains('size-5')).toBe(true);
    }

    expect(mounted.findAll('[role="img"]')).toHaveLength(1);
  });
});
