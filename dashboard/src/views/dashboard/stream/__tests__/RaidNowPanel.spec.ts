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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import RaidNowPanel from '../RaidNowPanel.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = {
  method: string;
  path: string;
  body?: unknown;
  query?: unknown;
};

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({
  write: true,
  unknownLogin: false,
  cancelFails: false,
}));

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (
      path: string,
      init: { params: { query: { login: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        query: init.params.query,
      });

      if (access.unknownLogin)
        return { error: {}, response: { ok: false, status: 404 } };

      return {
        data: {
          data: {
            id: '4242',
            login: init.params.query.login,
            displayName: 'Friend_Channel',
          },
        },
        response: { ok: true, status: 200 },
      };
    },
    POST: async (path: string, init: { body: unknown }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: init.body,
      });

      return { data: { data: {} }, response: { ok: true, status: 201 } };
    },
    DELETE: async (path: string): Promise<Answer> => {
      calls.push({ method: 'DELETE', path });

      return access.cancelFails
        ? { error: { detail: 'No raid' }, response: { ok: false, status: 400 } }
        : { response: { ok: true, status: 204 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: () => access.write }),
}));

const toasts = vi.hoisted(() => ({
  success: vi.fn<(text: string) => void>(),
  error: vi.fn<(text: string) => void>(),
}));

vi.mock('vue-sonner', () => ({ toast: toasts }));

const raidsPath = '/api/v{version}/channels/{channelId}/live-ops/raids';

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(RaidNowPanel, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

async function search(page: VueWrapper, login: string): Promise<void> {
  await page.get('[data-raid-input]').setValue(login);
  await page.get('[data-raid-form]').trigger('submit');
  await flushPromises();
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;
  access.unknownLogin = false;
  access.cancelFails = false;
  calls.length = 0;
  vi.clearAllMocks();
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
});

describe('RaidNowPanel', () => {
  it('looks the channel up, names it in the confirm and raids only after the confirm', async () => {
    const page = await open();

    await search(page, '  @friend  ');

    expect(calls).toHaveLength(1);
    expect(calls[0]).toMatchObject({ method: 'GET', query: { login: 'friend' } });
    expect(document.body.textContent).toContain('Raid Friend_Channel?');

    document.body.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(calls[1]).toEqual({
      method: 'POST',
      path: raidsPath,
      body: { targetTwitchBroadcasterId: '4242' },
    });

    expect(toasts.success).toHaveBeenCalledWith('The raid to Friend_Channel is counting down.');
    expect((page.get('[data-raid-input]').element as HTMLInputElement).value).toBe('');
  });

  it('sends nothing when the confirm is dismissed', async () => {
    const page = await open();

    await search(page, 'friend');
    document.body.querySelector<HTMLElement>('[data-confirm-cancel], [role="alertdialog"] button')?.click();
    await flushPromises();

    expect(calls.filter((call) => call.method === 'POST')).toEqual([]);
  });

  it('says so when Twitch has no such channel and opens no confirm', async () => {
    access.unknownLogin = true;
    const page = await open();

    await search(page, 'nobody');

    expect(page.get('[data-raid-missing]').text()).toBe('Twitch has no channel named "nobody".');
    expect(document.body.querySelector('[data-confirm-action]')).toBeNull();
  });

  it('cancels a raid that has not begun', async () => {
    const page = await open();

    await page.get('[data-raid-cancel]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([{ method: 'DELETE', path: raidsPath }]);
    expect(toasts.success).toHaveBeenCalledWith('The raid is canceled.');
  });

  it('tells the streamer when there is no raid to cancel', async () => {
    access.cancelFails = true;
    const page = await open();

    await page.get('[data-raid-cancel]').trigger('click');
    await flushPromises();

    expect(toasts.error).toHaveBeenCalledWith('There is no raid to cancel.');
  });

  it('shows nothing without the raid right', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-view="raid-now"]').exists()).toBe(false);
  });
});
