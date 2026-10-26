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

import OptionSelect from '@/components/layout/OptionSelect.vue';

import AutomodView from '../AutomodView.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: { body: Record<string, unknown> }) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: { body: Record<string, unknown> }) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

const twitchPath = '/api/v{version}/channels/{channelId}/moderation/automod/twitch';

const twitchLevels = {
  aggression: 1,
  bullying: 2,
  disability: 0,
  misogyny: 3,
  raceEthnicityOrReligion: 4,
  sexBasedTerms: 1,
  sexualitySexOrGender: 2,
  swearing: 0,
} as const satisfies Record<string, number>;

const access = vi.hoisted(() => ({ write: true }));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:automod:write' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

const stored = {
  linkFilter: { enabled: false, whitelist: ['twitch.tv'] },
  capsFilter: { enabled: true, threshold: 70 },
  bannedPhrases: { enabled: true, phrases: ['buy followers', 'free robux'] },
  emoteSpam: { enabled: false, maxEmotes: 10 },
  heatTimeoutThreshold: 55,
  autoTimeoutOnHeat: true,
  heatTimeoutSeconds: 900,
} as const satisfies Record<string, unknown>;

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(AutomodView, {
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;

  http.GET.mockImplementation(async (path) => ({
    data: { data: path === twitchPath
      ? { overallLevel: null, ...twitchLevels }
      : stored },
    response: { ok: true, status: 200 },
  }));

  http.PUT.mockImplementation(async (_path, init) => ({
    data: { data: { overallLevel: null, ...init.body } },
    response: { ok: true, status: 200 },
  }));

  http.POST.mockImplementation(async (_path, init) => ({
    data: { data: init.body },
    response: { ok: true, status: 200 },
  }));
});

afterEach(() => {
  wrapper?.unmount();
  vi.clearAllMocks();
});

describe('AutomodView', () => {
  it('shows the four saved filters', async () => {
    const page = await open();

    expect((page.get('[data-field="allowedDomains"]').element as HTMLTextAreaElement).value).toBe('twitch.tv');

    expect((page.get('[data-field="phrases"]').element as HTMLTextAreaElement).value).toBe(
      'buy followers\nfree robux',
    );

    expect((page.get('[data-field="capsThreshold"] input').element as HTMLInputElement).value).toBe('70');
    expect((page.get('[data-field="maxEmotes"] input').element as HTMLInputElement).value).toBe('10');
  });

  it('keeps Save off until something changes', async () => {
    const page = await open();

    expect(page.get('[data-action="save"]').attributes('disabled')).toBeDefined();
  });

  it('saves the edited list as one entry per line and sends the heat settings back unchanged', async () => {
    const page = await open();

    await page.get('[data-field="phrases"]').setValue('  buy followers \n\nfree robux\nfree robux\nnew phrase');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(http.POST).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/automod',
      expect.objectContaining({
        body: {
          ...stored,
          bannedPhrases: { enabled: true, phrases: ['buy followers', 'free robux', 'new phrase'] },
        },
      }),
    );
  });

  it('refuses a caps percent outside 1 to 100 before it reaches the bot', async () => {
    const page = await open();

    await page.get('[data-field="capsThreshold"] input').setValue('150');

    expect(page.get('[data-action="save"]').attributes('disabled')).toBeDefined();
    expect(http.POST).not.toHaveBeenCalled();
  });

  it('shows the level Twitch holds for each of the eight categories', async () => {
    const page = await open();

    const shown = page
      .findAll('[data-category]')
      .map((row) => [row.attributes('data-category'), row.text()]);

    expect(shown).toHaveLength(8);

    expect(shown.find(([category]) => category === 'raceEthnicityOrReligion')?.[1]).toContain(
      'Level 4, strictest',
    );

    expect(shown.find(([category]) => category === 'swearing')?.[1]).toContain('Off');
  });

  it('keeps Save Twitch levels off until a level changes and sends all eight categories', async () => {
    const page = await open();

    expect(page.get('[data-action="save-twitch"]').attributes('disabled')).toBeDefined();

    const row = page.get('[data-category="swearing"]');

    row.findComponent(OptionSelect).vm.$emit('update:modelValue', '3');
    await flushPromises();
    await page.get('[data-action="save-twitch"]').trigger('click');
    await flushPromises();

    expect(http.PUT).toHaveBeenCalledWith(twitchPath, expect.objectContaining({
      body: { ...twitchLevels, swearing: 3 },
    }));

    expect(page.get('[data-action="save-twitch"]').attributes('disabled')).toBeDefined();
  });

  it('is read-only without the write action', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-action="save"]').exists()).toBe(false);
  });
});
