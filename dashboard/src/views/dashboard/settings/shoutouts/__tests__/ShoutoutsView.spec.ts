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
import ShoutoutsView from '../ShoutoutsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const autoPath = '/api/v{version}/channels/{channelId}/moderation/auto-shoutout';
const templatePath = '/api/v{version}/channels/{channelId}/moderation/shoutout-template';

const store = vi.hoisted(() => ({ enabled: false, template: null as string | null }));
const access = vi.hoisted(() => ({ write: true }));
const puts = vi.hoisted(() => [] as { path: string; body: unknown }[]);

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (path: string): Promise<Answer> => {
      const answers = new Map<string, unknown>([
        [autoPath, { enabled: store.enabled }],
        [templatePath, { template: store.template }],
      ]);

      // Anything else is the helper list the template field asks for: none.
      return { data: { data: answers.get(path) ?? [] }, response: { ok: true, status: 200 } };
    },
    PUT: async (path: string, init: { body: Record<string, unknown> }): Promise<Answer> => {
      puts.push({ path, body: init.body });

      if (path.endsWith('auto-shoutout')) {
        store.enabled = init.body.enabled as boolean;
      } else {
        store.template = (init.body.template as string) === ''
          ? null
          : (init.body.template as string);
      }

      return { response: { ok: true, status: 204 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:shoutout' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(ShoutoutsView, {
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

const area = (page: VueWrapper): HTMLTextAreaElement =>
  page.get('[data-field="shoutoutTemplate"]').element as HTMLTextAreaElement;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  store.enabled = false;
  store.template = 'Go follow {target.displayname}!';
  access.write = true;
  puts.length = 0;
});

afterEach(() => {
  wrapper?.unmount();
  vi.clearAllMocks();
});

describe('ShoutoutsView', () => {
  it('shows the stored message and keeps Save off until it changes', async () => {
    const page = await open();

    expect(area(page).value).toBe('Go follow {target.displayname}!');
    expect(page.get('[data-action="save"]').attributes('disabled')).toBeDefined();
  });

  it('saves the edited message trimmed and shows the stored value after the refetch', async () => {
    const page = await open();

    await page.get('[data-field="shoutoutTemplate"]').setValue('  Hey {target.displayname}  ');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: templatePath, body: { template: 'Hey {target.displayname}' } }]);
    expect(store.template).toBe('Hey {target.displayname}');
  });

  it('clears the custom message with Use the default', async () => {
    const page = await open();

    await page.get('[data-action="use-default"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: templatePath, body: { template: '' } }]);
    expect(store.template).toBeNull();
    expect(page.get('[data-action="use-default"]').attributes('disabled')).toBeDefined();
  });

  it('turns automatic shoutouts on at once', async () => {
    const page = await open();

    await page.get('[data-field="autoShoutout"] button[role="switch"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: autoPath, body: { enabled: true } }]);
    expect(store.enabled).toBe(true);
  });

  it('is read-only without the shoutout right', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-action="save"]').exists()).toBe(false);
    expect(page.find('[data-action="use-default"]').exists()).toBe(false);
  });
});
