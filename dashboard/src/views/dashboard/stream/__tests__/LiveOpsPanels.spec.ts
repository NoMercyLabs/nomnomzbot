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
import { ref, type Component } from 'vue';

import { i18n } from '@/i18n';

import CommercialPanel from '../CommercialPanel.vue';
import en from '../i18n/en.json';
import RaidRulesPanel from '../RaidRulesPanel.vue';
import SchedulePanel from '../SchedulePanel.vue';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
  pathParams: unknown;
};

const calls: Call[] = [];

const segment = {
  id: 'seg1',
  startTime: '2026-10-20T18:00:00Z',
  endTime: '2026-10-20T20:00:00Z',
  title: 'Building the bot',
  category: { id: '1', name: 'Software and Game Development' },
  isRecurring: true,
  canceledUntil: null,
};

const rules = {
  preferredCategories: [{
    name: 'Just Chatting',
    weight: 5,
    label: 'Likes chatting',
  }],
  otherCategoryWeight: 0,
  otherCategoryLabel: 'Other',
  keywords: [],
  viewerBands: [],
  recency: {
    neverRaidedWeight: 3,
    steps: [],
    olderWeight: 1,
    olderNote: 'Long ago',
  },
  balance: {
    owedBaseWeight: 2,
    owedExtraWeight: 1,
    reciprocalWeight: 1,
  },
  followedWeight: 4,
};

const ok = (data: unknown): object => ({ data: { data }, response: { status: 200, ok: true } });

function answer(method: string, path: string, options?: Options): object {
  calls.push({
    method,
    path,
    body: options?.body,
    pathParams: options?.params?.path,
  });

  if (method === 'GET' && path.endsWith('/live-ops/schedule'))
    return ok({
      segments: [segment],
      vacation: null,
      broadcasterId: 'b',
    });

  if (method === 'GET' && path.endsWith('/scoring-rules'))
    return ok(rules);

  if (method === 'PUT' && path.endsWith('/scoring-rules'))
    return ok(options?.body);

  return ok(null);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => answer('GET', path, options),
    POST: async (path: string, options?: Options) => answer('POST', path, options),
    PUT: async (path: string, options?: Options) => answer('PUT', path, options),
    PATCH: async (path: string, options?: Options) => answer('PATCH', path, options),
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

async function show(panel: Component): Promise<void> {
  mounted = mount(panel, {
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

async function press(selector: string): Promise<void> {
  pick<HTMLButtonElement>(selector).click();
  await settle();
}

async function typeInto(selector: string, value: string): Promise<void> {
  const box = pick<HTMLInputElement>(selector);

  box.value = value;
  box.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

const sent = (method: string): Call[] => calls.filter((call) => call.method === method);

describe('the schedule panel', () => {
  it('cancels a segment with a PATCH that names the segment and sets isCanceled', async () => {
    await show(SchedulePanel);

    await press('[data-segment-cancel]');

    const patch = sent('PATCH')[0];

    expect(patch?.path).toBe('/api/v{version}/channels/{channelId}/live-ops/schedule/segment/{segmentId}');
    expect(patch?.pathParams).toMatchObject({ channelId: 'c1', segmentId: 'seg1' });
    expect(patch?.body).toEqual({ isCanceled: true });
  });

  it('turns the vacation off with only the switch value, and reads the schedule again', async () => {
    await show(SchedulePanel);
    const readsBefore = calls.filter((call) => call.method === 'GET').length;

    await press('[data-vacation-save]');

    expect(sent('PUT')[0]?.body).toEqual({ isVacationEnabled: false });
    expect(calls.filter((call) => call.method === 'GET').length).toBeGreaterThan(readsBefore);
  });
});

describe('the raid rules panel', () => {
  it('saves the whole rule set with the edited weight and keeps Save off until a change', async () => {
    await show(RaidRulesPanel);

    expect(pick<HTMLButtonElement>('[data-raid-rules-save]').disabled).toBe(true);

    await typeInto('#followed', '9');
    expect(pick<HTMLButtonElement>('[data-raid-rules-save]').disabled).toBe(false);

    await press('[data-raid-rules-save]');

    expect(sent('PUT')[0]?.body).toEqual({ ...rules, followedWeight: 9 });
  });
});

describe('the commercial panel', () => {
  it('starts an ad with the chosen length in seconds', async () => {
    await show(CommercialPanel);

    const lengths = [...document.body.querySelectorAll<HTMLButtonElement>('[data-commercial-length]')];

    lengths[2]?.click();
    await settle();
    await press('[data-commercial-run]');

    expect(sent('POST')[0]?.path).toBe('/api/v{version}/channels/{channelId}/live-ops/ads/commercial');
    expect(sent('POST')[0]?.body).toEqual({ lengthSeconds: 90 });
  });
});
