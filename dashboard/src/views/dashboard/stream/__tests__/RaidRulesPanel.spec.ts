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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import RaidRulesPanel from '../RaidRulesPanel.vue';

type Params = { path?: Record<string, unknown>; query?: Record<string, unknown> };
type Options = { params?: Params; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];

const manyWords = Array.from({ length: 128 }, (_word, index) =>
  `word${index}`);

// The shape of a real channel's saved rules: two liked games, two word rules, six viewer bands.
const rules = {
  preferredCategories: [
    {
      name: 'Software and Game Development',
      weight: 60,
      label: 'Builds software',
    },
    {
      name: 'Just Chatting',
      weight: 40,
      label: 'Likes chatting',
    },
  ],
  otherCategoryWeight: -20,
  otherCategoryLabel: 'Other game',
  keywords: [
    {
      label: 'Game making',
      weight: -40,
      words: ['game making'],
      matchTags: true,
      matchTitle: true,
    },
    {
      label: 'Software',
      weight: 35,
      words: manyWords,
      matchTags: true,
      matchTitle: false,
    },
  ],
  viewerBands: [
    {
      min: 0,
      max: 0,
      weight: 0,
      label: 'Nobody',
    },
    {
      min: 1,
      max: 5,
      weight: 10,
      label: 'Tiny',
    },
    {
      min: 6,
      max: 20,
      weight: 20,
      label: 'Small',
    },
    {
      min: 21,
      max: 50,
      weight: 5,
      label: 'Medium',
    },
    {
      min: 51,
      max: 200,
      weight: -5,
      label: 'Large',
    },
    {
      min: 201,
      max: 100000,
      weight: -15,
      label: 'Huge',
    },
  ],
  recency: {
    neverRaidedWeight: 30,
    steps: [
      {
        withinDays: 7,
        weight: -40,
        note: 'Raided this week',
      },
      {
        withinDays: 14,
        weight: -15,
        note: 'Raided two weeks ago',
      },
      {
        withinDays: 30,
        weight: 5,
        note: 'Raided this month',
      },
    ],
    olderWeight: 25,
    olderNote: 'A while ago',
  },
  balance: {
    owedBaseWeight: 55,
    owedExtraWeight: 12,
    reciprocalWeight: 10,
  },
  followedWeight: 50,
};

const ok = (data: unknown): object => ({ data: { data }, response: { status: 200, ok: true } });

function answer(method: string, path: string, options?: Options): object {
  calls.push({
    method,
    path,
    body: options?.body,
  });

  if (method === 'GET' && path.endsWith('/scoring-rules'))
    return ok(rules);

  if (method === 'PUT' && path.endsWith('/scoring-rules'))
    return ok(options?.body);

  if (method === 'GET' && path.endsWith('/stream/categories'))
    return ok([{
      id: '9',
      name: 'Fortnite',
      boxArtUrl: '',
    }]);

  return ok(null);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => answer('GET', path, options),
    PUT: async (path: string, options?: Options) => answer('PUT', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: ref({
      login: 'stoney_eagle',
      displayName: 'Stoney_Eagle',
      isLive: false,
    }),
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (): boolean => true,
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function show(): Promise<void> {
  mounted = mount(RaidRulesPanel, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const pick = <T extends Element>(selector: string): T =>
  document.body.querySelector<T>(selector) as T;

const pickAll = <T extends Element>(selector: string): T[] => [
  ...document.body.querySelectorAll<T>(selector),
];

async function press(selector: string): Promise<void> {
  pick<HTMLButtonElement>(selector).click();
  await settle();
}

async function typeInto(box: HTMLInputElement, value: string): Promise<void> {
  box.value = value;
  box.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

const lastPut = (): Call | undefined => calls.filter((call) => call.method === 'PUT').at(-1);

describe('the raid rules card', () => {
  it('says what a point does before the first rule', async () => {
    await show();

    expect(pick('[data-raid-intro]').textContent).toContain('Higher points are suggested first');
    expect(pick('[data-raid-intro]').textContent).toContain('0 does nothing');
  });

  it('shows each saved category in full with its points and a word for their strength', async () => {
    await show();

    const rows = pickAll<HTMLElement>('[data-category-rule]');
    const names = rows.map((row) => row.querySelector('[data-category-picker] p')?.textContent?.trim());
    const points = rows.map((row) => row.querySelector<HTMLInputElement>('input[data-points]')?.value);
    const strength = rows.map((row) => row.querySelector('[data-strength]')?.textContent?.trim());

    expect(names).toEqual(['Software and Game Development', 'Just Chatting']);
    expect(points).toEqual(['60', '40']);
    expect(strength).toEqual(['Strong plus', 'Medium plus']);
  });

  it('labels every viewer band field instead of showing bare numbers', async () => {
    await show();

    const band = pickAll<HTMLElement>('[data-viewer-band]')[1] as HTMLElement;
    const labels = [...band.querySelectorAll('input')].map((box) => box.getAttribute('aria-label'));

    expect(labels).toEqual(['Fewest viewers', 'Most viewers', 'Points', 'Reason shown']);
  });

  it('folds a long word list into a count and opens it on demand', async () => {
    await show();

    const rule = pickAll<HTMLElement>('[data-keyword-rule]')[1] as HTMLElement;

    expect(rule.querySelector('[data-words-count]')?.textContent).toContain('128');
    expect(rule.querySelectorAll('[data-word]').length).toBeLessThan(128);

    rule.querySelector<HTMLButtonElement>('[data-words-toggle]')?.click();
    await settle();

    expect(rule.querySelectorAll('[data-word]')).toHaveLength(128);
  });

  it('sends only the changed value when one band gets other points', async () => {
    await show();

    const band = pickAll<HTMLElement>('[data-viewer-band]')[2] as HTMLElement;

    await typeInto(band.querySelector<HTMLInputElement>('input[data-points]') as HTMLInputElement, '35');
    await press('[data-raid-rules-save]');

    const viewerBands = rules.viewerBands.map((entry, index) =>
      index === 2
        ? { ...entry, weight: 35 }
        : entry,
    );

    expect(lastPut()?.body).toEqual({ ...rules, viewerBands });
  });

  it('sends a new category after it was picked from the Twitch search and given points', async () => {
    await show();

    await press('[data-category-add]');

    const rows = pickAll<HTMLElement>('[data-category-rule]');

    expect(rows).toHaveLength(3);

    const added = rows[2] as HTMLElement;

    await typeInto(added.querySelector('input') as HTMLInputElement, 'fort');
    await new Promise((done) => setTimeout(done, 400));
    await settle();
    added.querySelector<HTMLButtonElement>('[data-category-result]')?.click();
    await settle();
    await typeInto(added.querySelector<HTMLInputElement>('input[data-points]') as HTMLInputElement, '15');
    await press('[data-raid-rules-save]');

    expect(lastPut()?.body).toEqual({
      ...rules,
      preferredCategories: [
        ...rules.preferredCategories,
        {
          name: 'Fortnite',
          weight: 15,
          label: '',
        },
      ],
    });
  });

  it('steps points by 5 with the plus button', async () => {
    await show();

    const band = pickAll<HTMLElement>('[data-viewer-band]')[0] as HTMLElement;

    band.querySelector<HTMLButtonElement>('[data-points-up]')?.click();
    await settle();
    await press('[data-raid-rules-save]');

    const viewerBands = rules.viewerBands.map((entry, index) =>
      index === 0
        ? { ...entry, weight: 5 }
        : entry,
    );

    expect(lastPut()?.body).toEqual({ ...rules, viewerBands });
  });
});
