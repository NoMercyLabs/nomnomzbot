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

import EgressAllowlistCard from '../components/EgressAllowlistCard.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = {
  method: string;
  path: string;
  body?: unknown;
  allowlistId?: string;
};

const listPath = '/api/v{version}/channels/{channelId}/egress-allowlist';
const onePath = `${listPath}/{allowlistId}`;
const enabledPath = `${onePath}/enabled`;
const blastPath = `${onePath}/blast-radius`;

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({ write: true, blastFails: false }));
const store = vi.hoisted(() => ({ hosts: [] as Record<string, unknown>[] }));

const host = (id: string, name: string, isEnabled = true): Record<string, unknown> => ({
  id,
  host: name,
  isEnabled,
  approvedByUserId: null,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
});

const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (path: string): Promise<Answer> => {
      if (path === blastPath) {
        return access.blastFails
          ? { error: { detail: 'down' }, response: { ok: false, status: 500 } }
          : ok({
              categories: [{
                categoryKey: 'outbound_webhooks',
                count: 2,
                sample: ['Home server', 'Backup'],
              }],
              isMinimum: false,
            });
      }

      return ok(store.hosts);
    },
    POST: async (path: string, init: { body: { host: string } }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: init.body,
      });

      store.hosts = [...store.hosts, host('new', init.body.host)];

      return ok(null);
    },
    PUT: async (
      path: string,
      init: { body: { isEnabled: boolean }; params: { path: { allowlistId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: init.body,
        allowlistId: init.params.path.allowlistId,
      });

      store.hosts = store.hosts.map((row) =>
        row.id === init.params.path.allowlistId
          ? { ...row, ...init.body }
          : row);

      return ok(null);
    },
    DELETE: async (
      path: string,
      init: { params: { path: { allowlistId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        allowlistId: init.params.path.allowlistId,
      });

      store.hosts = store.hosts.filter((row) => row.id !== init.params.path.allowlistId);

      return { response: { ok: true, status: 204 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'egress:allowlist:write' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(EgressAllowlistCard, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;
  access.blastFails = false;
  calls.length = 0;
  store.hosts = [host('h1', 'hooks.example.com'), host('h2', 'old.example.com', false)];
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

describe('EgressAllowlistCard', () => {
  it('lists each approved host and says which one is switched off', async () => {
    const page = await open();

    expect(page.findAll('[data-host]').map((row) => row.attributes('data-host'))).toEqual([
      'hooks.example.com',
      'old.example.com',
    ]);

    expect(page.get('[data-host="old.example.com"]').text()).toContain('Switched off');
    expect(page.get('[data-host="hooks.example.com"]').text()).not.toContain('Switched off');
  });

  it('adds a trimmed host, clears the field and shows the new row', async () => {
    const page = await open();

    await page.get('[data-host-input]').setValue('  new.example.com  ');
    await page.get('[data-add-form]').trigger('submit');
    await flushPromises();

    expect(calls).toEqual([{
      method: 'POST',
      path: listPath,
      body: { host: 'new.example.com' },
    }]);

    expect((page.get('[data-host-input]').element as HTMLInputElement).value).toBe('');
    expect(page.find('[data-host="new.example.com"]').exists()).toBe(true);
  });

  it('does not send an empty host', async () => {
    const page = await open();

    await page.get('[data-add-form]').trigger('submit');
    await flushPromises();

    expect(calls).toEqual([]);
    expect(page.get('[data-add]').attributes('disabled')).toBeDefined();
  });

  it('switches a host on through a one-field update', async () => {
    const page = await open();

    await page.get('[data-host-enabled="old.example.com"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: enabledPath,
        body: { isEnabled: true },
        allowlistId: 'h2',
      },
    ]);

    expect(page.get('[data-host="old.example.com"]').text()).not.toContain('Switched off');
  });

  it('counts what relies on a host, then removes it only after the confirm', async () => {
    const page = await open();

    await page.get('[data-host-remove="hooks.example.com"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([]);
    expect(document.body.textContent).toContain('2 ');
    expect(document.body.textContent).toContain('Home server, Backup');

    document.body.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(calls).toEqual([{
      method: 'DELETE',
      path: onePath,
      allowlistId: 'h1',
    }]);

    expect(page.findAll('[data-host]').map((row) => row.attributes('data-host'))).toEqual([
      'old.example.com',
    ]);
  });

  it('offers no removal when the bot cannot count what relies on the host', async () => {
    access.blastFails = true;
    const page = await open();

    await page.get('[data-host-remove="hooks.example.com"]').trigger('click');
    await flushPromises();

    expect(document.body.querySelector('[data-confirm-action]')).toBeNull();
    expect(calls).toEqual([]);
    expect(page.find('[data-host="hooks.example.com"]').exists()).toBe(true);
  });

  it('is read-only without the write action', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-add-form]').exists()).toBe(false);
    expect(page.find('[data-host-enabled]').exists()).toBe(false);
    expect(page.find('[data-host-remove]').exists()).toBe(false);
    expect(page.findAll('[data-host]')).toHaveLength(2);
  });
});
