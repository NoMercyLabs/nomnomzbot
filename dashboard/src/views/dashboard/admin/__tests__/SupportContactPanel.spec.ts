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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import SupportContactPanel from '../components/SupportContactPanel.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls = vi.hoisted(() => [] as Call[]);
const state = vi.hoisted(() => ({
  url: null as string | null,
  may: true,
  refuse: false,
}));

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (): Promise<Answer> => ({
      data: { data: { url: state.url } },
      response: { ok: true, status: 200 },
    }),
    PUT: async (path: string, init: { body: { url: string } }): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: init.body,
      });

      if (state.refuse)
        return { error: { detail: 'Use an absolute http or https address.' }, response: { ok: false, status: 400 } };

      state.url = init.body.url === ''
        ? null
        : init.body.url;

      return { data: { data: { url: state.url } }, response: { ok: true, status: 200 } };
    },
  },
}));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => state.may }),
}));

const toasts = vi.hoisted(() => ({
  success: vi.fn<(text: string) => void>(),
  error: vi.fn<(text: string) => void>(),
}));

vi.mock('vue-sonner', () => ({ toast: toasts }));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(SupportContactPanel, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  state.url = 'https://example.com/help';
  state.may = true;
  state.refuse = false;
  calls.length = 0;
  vi.clearAllMocks();
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
});

const field = (page: VueWrapper): HTMLInputElement =>
  page.get('[data-support-url]').element as HTMLInputElement;

describe('SupportContactPanel', () => {
  it('shows the stored address and keeps Save off until the address changes', async () => {
    const page = await open();

    expect(field(page).value).toBe('https://example.com/help');
    expect(page.get('[data-support-save]').attributes('disabled')).toBeDefined();
  });

  it('saves the trimmed address and keeps it', async () => {
    const page = await open();

    await page.get('[data-support-url]').setValue('  https://example.com/new  ');
    await page.get('form').trigger('submit');
    await flushPromises();

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/admin/support-contact',
        body: { url: 'https://example.com/new' },
      },
    ]);

    expect(toasts.success).toHaveBeenCalledWith('The support contact is saved.');
    expect(field(page).value).toBe('https://example.com/new');
  });

  it('clears the contact when the field is emptied', async () => {
    const page = await open();

    await page.get('[data-support-url]').setValue('');
    await page.get('form').trigger('submit');
    await flushPromises();

    expect(calls.map((call) => call.body)).toEqual([{ url: '' }]);
    expect(state.url).toBeNull();
  });

  it('shows the bot reason when it refuses the address', async () => {
    state.refuse = true;
    const page = await open();

    await page.get('[data-support-url]').setValue('ftp://nope');
    await page.get('form').trigger('submit');
    await flushPromises();

    expect(toasts.success).not.toHaveBeenCalled();
    expect(toasts.error).toHaveBeenCalledTimes(1);
  });

  it('is read-only without the suspend right', async () => {
    state.may = false;
    const page = await open();

    expect(field(page).value).toBe('https://example.com/help');
    expect(field(page).disabled).toBe(true);
    expect(page.find('[data-support-save]').exists()).toBe(false);
  });
});
