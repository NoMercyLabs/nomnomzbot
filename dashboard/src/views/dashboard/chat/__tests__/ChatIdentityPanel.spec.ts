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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import type { ChatIdentity } from '@/api/chatIdentity';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ChatIdentityPanel from '../components/ChatIdentityPanel.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: vi.fn<() => void>(),
}));

vi.mock('@/api/client', () => ({ client: http }));
vi.mock('@/api/access', () => ({ useChannelAccess: () => ({ can: () => true }) }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];

const identity: ChatIdentity = {
  displayName: 'Stoney_Eagle',
  color: '#0000FF',
  canManageColor: true,
  colorScope: 'user:manage:chat_color',
  namedColors: [
    { name: 'blue', hex: '#0000FF' },
    { name: 'hot_pink', hex: '#FF69B4' },
  ],
  badges: [],
};

let mounted: VueWrapper | null = null;

async function open(answer: ChatIdentity): Promise<VueWrapper> {
  http.GET.mockResolvedValue({ data: { data: answer }, response: ok });

  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(ChatIdentityPanel, {
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  http.GET.mockReset();
  http.PUT.mockReset();
  http.POST.mockReset();
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

describe('Chat identity panel', () => {
  it('sends the color name when a swatch is clicked', async () => {
    http.PUT.mockResolvedValue({ data: { data: identity }, response: ok });

    const view = await open(identity);

    await view.get('[data-test="swatch-hot_pink"]').trigger('click');
    await flushPromises();

    const [, init] = http.PUT.mock.calls[0] as [string, { body: unknown }];

    expect(init.body).toEqual({ color: 'hot_pink' });
  });

  it('shows the reason when Twitch refuses a hex color', async () => {
    http.PUT.mockResolvedValue({
      error: { status: 'error', message: 'Hex colors need Turbo or Prime.' },
      response: { ok: false, status: 403 },
    });

    const view = await open(identity);

    await view.get('[data-test="hex"]').setValue('123456');
    await view.get('form').trigger('submit');
    await flushPromises();

    const [, init] = http.PUT.mock.calls[0] as [string, { body: unknown }];

    expect(init.body).toEqual({ color: '#123456' });
    expect(view.get('[data-test="refusal"]').text()).not.toBe('');
  });

  it('offers re-consent and disables the swatches when the scope is missing', async () => {
    const view = await open({ ...identity, canManageColor: false });

    expect(view.find('[data-test="regrant"]').exists()).toBe(true);
    expect(view.get('[data-test="swatch-blue"]').attributes('disabled')).toBeDefined();
  });
});
