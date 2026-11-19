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
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import UnbanRequestsView from '../UnbanRequestsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Resolve = {
  id: string;
  body: { approve: boolean; confirm?: boolean };
};

const resolved = vi.hoisted(() => [] as Resolve[]);
const access = vi.hoisted(() => ({ resolve: true }));
const store = vi.hoisted(() => ({ requests: [] as Record<string, unknown>[] }));

const request = (id: string, userName: string, text: string): Record<string, unknown> => ({
  id,
  userId: `u-${id}`,
  userLogin: userName.toLowerCase(),
  userName,
  text,
  status: 'pending',
  createdAt: '2026-11-18T10:00:00Z',
});

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (): Promise<Answer> => ({
      data: { data: store.requests },
      response: { ok: true, status: 200 },
    }),
    POST: async (
      _path: string,
      init: { params: { path: { unbanRequestId: string } }; body: Resolve['body'] },
    ): Promise<Answer> => {
      resolved.push({ id: init.params.path.unbanRequestId, body: init.body });
      store.requests = store.requests.filter((row) => row.id !== init.params.path.unbanRequestId);

      return { data: { data: {} }, response: { ok: true, status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:unbanrequest:resolve' || access.resolve }),
}));

async function mountView(): Promise<VueWrapper> {
  i18n.global.mergeLocaleMessage('en', en);
  const wrapper = mount(UnbanRequestsView, {
    global: {
      plugins: [createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], i18n],
      stubs: { SettingsPageFrame: { template: '<div><slot /></div>' } },
    },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  resolved.length = 0;
  access.resolve = true;
  store.requests = [request('r1', 'Meeper', 'I am sorry'), request('r2', 'Spammer', 'unban me')];
});

describe('the unban requests page', () => {
  it('lists every pending request with what the person wrote', async () => {
    const wrapper = await mountView();

    expect(wrapper.findAll('[data-request]').map((row) => row.attributes('data-request'))).toEqual(['r1', 'r2']);
    expect(wrapper.text()).toContain('I am sorry');
  });

  it('denies at once, and the request leaves the list', async () => {
    const wrapper = await mountView();

    await wrapper.find('[data-request-deny="r2"]').trigger('click');
    await flushPromises();

    expect(resolved).toEqual([{ id: 'r2', body: { approve: false } }]);
    expect(wrapper.findAll('[data-request]').map((row) => row.attributes('data-request'))).toEqual(['r1']);
  });

  it('approves only after the second press confirms it, and sends that confirm', async () => {
    const wrapper = await mountView();

    await wrapper.find('[data-request-approve="r1"]').trigger('click');
    await flushPromises();

    expect(resolved).toEqual([]);

    await wrapper.find('[data-request-confirm="r1"]').trigger('click');
    await flushPromises();

    expect(resolved).toEqual([{ id: 'r1', body: { approve: true, confirm: true } }]);
  });

  it('shows no resolve buttons to a person who may only read', async () => {
    access.resolve = false;
    const wrapper = await mountView();

    expect(wrapper.find('[data-request-approve]').exists()).toBe(false);
    expect(wrapper.find('[data-request-deny]').exists()).toBe(false);
  });
});
