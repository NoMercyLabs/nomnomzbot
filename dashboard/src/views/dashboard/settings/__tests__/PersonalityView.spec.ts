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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { personalityKey } from '@/api/personalitySettings';
import { i18n } from '@/i18n';
import { useSaveErrorsStore } from '@/stores/saveErrors';

import en from '../i18n/en.json';
import PersonalityView from '../PersonalityView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const tones = ['informative', 'friendly', 'sassy', 'hype', 'chill'];
const puts: unknown[] = [];
let putAnswer: () => Answer;

const ok = (personality: string): Answer => ({
  data: { data: { personality, available: tones } },
  response: { status: 200, ok: true },
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (): Promise<Answer> => ok('informative'),
    PUT: async (_path: string, init: { body: unknown }): Promise<Answer> => {
      puts.push(init.body);

      return putAnswer();
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let queries: QueryClient;

beforeEach(() => {
  puts.length = 0;
  putAnswer = () => ok('sassy');
  pinia = createPinia();
  setActivePinia(pinia);
  queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  mounted = mount(PersonalityView, {
    attachTo: document.body,
    global: { plugins: [pinia, i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
  await vi.waitFor(() => expect(document.querySelectorAll('[data-tone]')).toHaveLength(5));
}

const radio = (tone: string): HTMLElement =>
  document.querySelector<HTMLElement>(`[data-tone="${tone}"] [role="radio"]`)!;

describe('PersonalityView', () => {
  it('lists the tones the bot offers with a one-line explanation, the saved one checked', async () => {
    await open();

    const rows = [...document.querySelectorAll('[data-tone]')];

    expect(rows.map((row: Element) => row.getAttribute('data-tone'))).toEqual(tones);
    expect(rows[0]!.textContent).toContain('Clear, helpful and polite');
    expect(rows[2]!.textContent).toContain('Sassy');
    expect(radio('informative').getAttribute('aria-checked')).toBe('true');
    expect(radio('sassy').getAttribute('aria-checked')).toBe('false');
  });

  it('sends the picked tone and shows the tone the bot stored', async () => {
    await open();

    radio('sassy').click();
    await flushPromises();

    expect(puts).toEqual([{ personality: 'sassy' }]);
    expect(radio('sassy').getAttribute('aria-checked')).toBe('true');

    expect(queries.getQueryData(personalityKey('c1'))).toEqual({
      personality: 'sassy',
      available: tones,
    });
  });

  it('does not send the tone that is already saved', async () => {
    await open();

    radio('informative').click();
    await flushPromises();

    expect(puts).toEqual([]);
  });

  it('puts the old tone back and reports the save when the bot refuses it', async () => {
    putAnswer = () => ({ error: { status: 'error' }, response: { status: 403, ok: false } });

    await open();

    radio('hype').click();
    await flushPromises();

    expect(puts).toEqual([{ personality: 'hype' }]);
    expect(radio('informative').getAttribute('aria-checked')).toBe('true');
    expect(radio('hype').getAttribute('aria-checked')).toBe('false');

    expect(useSaveErrorsStore().items[0]?.what).toEqual({
      key: 'setPersonality',
      params: { tone: 'hype' },
    });
  });
});
