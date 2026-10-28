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

import OptionSelect from '@/components/layout/OptionSelect.vue';
import { i18n } from '@/i18n';

import ChatFiltersView from '../ChatFiltersView.vue';
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
  filterId?: string;
};

const listPath = '/api/v{version}/channels/{channelId}/moderation/chat-filters';
const onePath = `${listPath}/{filterId}`;
const testPath = `${listPath}/test`;

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({ write: true }));
const store = vi.hoisted(() => ({ filters: [] as Record<string, unknown>[] }));

const filter = (id: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  id,
  filterType: 'Blocklist',
  name: `Filter ${id}`,
  pattern: null,
  terms: ['spoiler'],
  action: 'Delete',
  timeoutSeconds: null,
  exemptMinRoleLevel: 10,
  isEnabled: true,
  isCaseSensitive: false,
  matchCount: 7,
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
  linkPolicy: null,
  ...over,
});

const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (): Promise<Answer> => ok(store.filters),
    POST: async (path: string, init: { body: unknown }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: init.body,
      });

      return path === testPath
        ? ok({ isMatch: true, compileError: null })
        : ok(filter('new', init.body as Record<string, unknown>));
    },
    PUT: async (
      path: string,
      init: { body: Record<string, unknown>; params: { path: { filterId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: init.body,
        filterId: init.params.path.filterId,
      });

      store.filters = store.filters.map((row) =>
        row.id === init.params.path.filterId
          ? { ...row, ...init.body }
          : row);

      return ok(null);
    },
    DELETE: async (
      path: string,
      init: { params: { path: { filterId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        filterId: init.params.path.filterId,
      });

      store.filters = store.filters.filter((row) => row.id !== init.params.path.filterId);

      return { response: { ok: true, status: 204 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:filter:write' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(ChatFiltersView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

const pick = (page: VueWrapper, field: string, value: string): void => {
  const select = page
    .findAllComponents(OptionSelect)
    .find((candidate) => candidate.element.matches(`[data-field="${field}"]`) ||
      candidate.find(`[data-field="${field}"]`).exists());

  select?.vm.$emit('update:modelValue', value);
};

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;
  calls.length = 0;

  store.filters = [filter('f1'), filter('f2', {
    isEnabled: false,
    filterType: 'Regex',
    pattern: 'x+',
  })];
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
  vi.clearAllMocks();
});

describe('ChatFiltersView', () => {
  it('lists each filter with its type, action and match count', async () => {
    const page = await open();

    expect(page.get('[data-filter="f1"]').text()).toContain('Blocked terms');
    expect(page.get('[data-filter="f1"]').text()).toContain('Delete the message');
    expect(page.get('[data-filter="f1"]').text()).toContain('7 matches');
    expect(page.findAll('[data-filter]')).toHaveLength(2);
  });

  it('switches a filter on through a one-field update', async () => {
    const page = await open();

    await page.get('[data-filter-enabled="f2"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([{
      method: 'PUT',
      path: onePath,
      body: { isEnabled: true },
      filterId: 'f2',
    }]);

    expect(page.get('[data-filter-enabled="f2"]').attributes('data-state')).toBe('checked');
  });

  it('removes a filter only after the confirm and shows the shorter list', async () => {
    const page = await open();

    await page.get('[data-filter-remove="f1"]').trigger('click');
    await flushPromises();
    expect(calls).toEqual([]);

    document.body.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(calls).toEqual([{
      method: 'DELETE',
      path: onePath,
      filterId: 'f1',
    }]);

    expect(page.findAll('[data-filter]').map((row) => row.attributes('data-filter'))).toEqual(['f2']);
  });

  it('creates a blocklist filter with the terms one per line and the moderator floor by default', async () => {
    const page = await open();

    await page.get('[data-action="new"]').trigger('click');
    await page.get('[data-field="name"]').setValue('  No spoilers  ');
    await page.get('[data-field="terms"]').setValue('spoiler\n\n  leak  \nspoiler');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([
      {
        method: 'POST',
        path: listPath,
        body: {
          filterType: 'Blocklist',
          name: 'No spoilers',
          action: 'Delete',
          pattern: null,
          terms: ['spoiler', 'leak'],
          linkPolicy: null,
          timeoutSeconds: null,
          exemptMinRoleLevel: 10,
          isEnabled: true,
          isCaseSensitive: false,
        },
      },
    ]);
  });

  it('refuses a filter with no name or no terms before it reaches the bot', async () => {
    const page = await open();

    await page.get('[data-action="new"]').trigger('click');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([]);
    expect(page.text()).toContain('Give the filter a name.');
    expect(page.text()).toContain('Add at least one term.');
  });

  it('sends a timeout length only with a timeout action and a link policy only for a link filter', async () => {
    const page = await open();

    await page.get('[data-action="new"]').trigger('click');
    await page.get('[data-field="name"]').setValue('Links');
    pick(page, 'filterType', 'LinkPolicy');
    pick(page, 'action', 'Timeout');
    await flushPromises();
    await page.get('[data-field="allowedDomains"]').setValue('twitch.tv');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(calls[0]?.body).toMatchObject({
      filterType: 'LinkPolicy',
      action: 'Timeout',
      timeoutSeconds: 600,
      pattern: null,
      terms: null,
      linkPolicy: { allowedDomains: ['twitch.tv'], matchBareDomains: true },
    });
  });

  it('opens a filter for editing with its stored values and saves it by id', async () => {
    const page = await open();

    await page.get('[data-filter-edit="f2"]').trigger('click');
    expect((page.get('[data-field="pattern"]').element as HTMLInputElement).value).toBe('x+');

    await page.get('[data-field="pattern"]').setValue('y+');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([
      expect.objectContaining({
        method: 'PUT',
        path: onePath,
        filterId: 'f2',
        body: expect.objectContaining({ pattern: 'y+' }),
      }),
    ]);
  });

  it('tries a sample message against the filter without saving it', async () => {
    const page = await open();

    await page.get('[data-action="new"]').trigger('click');
    await page.get('[data-field="terms"]').setValue('spoiler');
    await page.get('[data-field="sample"]').setValue('a spoiler here');
    await page.get('[data-action="try"]').trigger('click');
    await flushPromises();

    expect(calls).toEqual([
      {
        method: 'POST',
        path: testPath,
        body: {
          filterType: 'Blocklist',
          pattern: null,
          terms: ['spoiler'],
          linkPolicy: null,
          sampleMessage: 'a spoiler here',
          isCaseSensitive: false,
        },
      },
    ]);

    expect(page.get('[data-verdict]').text()).toBe('This message would match.');
  });

  it('is read-only without the write action', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-action="new"]').exists()).toBe(false);
    expect(page.find('[data-filter-edit]').exists()).toBe(false);
    expect(page.find('[data-filter-remove]').exists()).toBe(false);
    expect(page.find('[data-filter-enabled]').exists()).toBe(false);
    expect(page.get('[data-filter-state="f1"]').text()).toBe('On');
    expect(page.get('[data-filter-state="f2"]').text()).toBe('Off');
  });
});
