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

import { i18n } from '@/i18n';

import BlockedTermsView from '../BlockedTermsView.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: { body: { term: string } }) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: { params: { path: { term: string } } }) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

const access = vi.hoisted(() => ({ write: true }));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:blocklist:write' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

const ok = (data: string[]): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(BlockedTermsView, {
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;
  http.GET.mockResolvedValue(ok(['spoiler', 'free robux']));
});

afterEach(() => {
  wrapper?.unmount();
  vi.clearAllMocks();
});

describe('BlockedTermsView', () => {
  it('lists the terms Twitch holds', async () => {
    const page = await open();

    expect(page.findAll('[data-term]').map((row) => row.attributes('data-term'))).toEqual([
      'spoiler',
      'free robux',
    ]);
  });

  it('blocks a trimmed term, shows the list Twitch answers with, and clears the field', async () => {
    http.POST.mockResolvedValue(ok(['spoiler', 'free robux', 'new term']));
    const page = await open();

    await page.get('[data-term-input]').setValue('  new term ');
    await page.get('[data-add-form]').trigger('submit');
    await flushPromises();

    expect(http.POST).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/blocked-terms',
      expect.objectContaining({ body: { term: 'new term' } }),
    );

    expect(page.findAll('[data-term]').map((row) => row.attributes('data-term'))).toContain(
      'new term',
    );

    expect((page.get('[data-term-input]').element as HTMLInputElement).value).toBe('');
  });

  it('keeps Block disabled until there is a term', async () => {
    const page = await open();

    expect(page.get('[data-add]').attributes('disabled')).toBeDefined();
    expect(http.POST).not.toHaveBeenCalled();
  });

  it('unblocks the term of the row it was pressed on and shows the new list', async () => {
    http.DELETE.mockResolvedValue(ok(['free robux']));
    const page = await open();

    await page.get('[data-term-remove="spoiler"]').trigger('click');
    await flushPromises();

    expect(http.DELETE).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/blocked-terms/{term}',
      expect.objectContaining({ params: { path: expect.objectContaining({ term: 'spoiler' }) } }),
    );

    expect(page.findAll('[data-term]').map((row) => row.attributes('data-term'))).toEqual([
      'free robux',
    ]);
  });

  it('is read-only without the write action', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-add-form]').exists()).toBe(false);
    expect(page.find('[data-term-remove]').exists()).toBe(false);
    expect(page.findAll('[data-term]')).toHaveLength(2);
  });
});
