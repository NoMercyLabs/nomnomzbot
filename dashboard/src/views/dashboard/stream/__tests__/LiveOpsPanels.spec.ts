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
import { ref, type Component } from 'vue';

import { i18n } from '@/i18n';

import CommercialPanel from '../CommercialPanel.vue';
import en from '../i18n/en.json';
import RaidRulesPanel from '../RaidRulesPanel.vue';
import SchedulePanel from '../SchedulePanel.vue';

type Params = { path?: Record<string, unknown>; query?: Record<string, unknown> };
type Options = { params?: Params; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
  pathParams: unknown;
  query: unknown;
};

const calls: Call[] = [];
const refusal = { body: null as object | null };

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

function answer(method: string, path: string, options?: Options) {
  calls.push({
    method,
    path,
    body: options?.body,
    pathParams: options?.params?.path,
    query: options?.params?.query,
  });

  if (method === 'GET' && path.endsWith('/live-ops/schedule'))
    return ok({
      segments: [segment],
      vacation: null,
      broadcasterId: 'b',
    });

  if (method === 'GET' && path.endsWith('/scoring-rules'))
    return ok(rules);

  if (method === 'PUT' && path.endsWith('/scoring-rules') && refusal.body !== null)
    return { error: refusal.body, response: { status: 400, ok: false } } as const satisfies object;

  if (method === 'PUT' && path.endsWith('/scoring-rules'))
    return ok(options?.body);

  if (method === 'GET' && path.endsWith('/stream/categories'))
    return ok([{
      id: '2',
      name: 'Software and Game Development',
      boxArtUrl: '',
    }]);

  return ok(null);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => answer('GET', path, options),
    POST: async (path: string, options?: Options) => answer('POST', path, options),
    PUT: async (path: string, options?: Options) => answer('PUT', path, options),
    PATCH: async (path: string, options?: Options) => answer('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => answer('DELETE', path, options),
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

const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

const writeKeys = ['live-ops:schedule:write', 'live-ops:raids:write', 'live-ops:ads:write'];

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  access.held = writeKeys;
  calls.length = 0;
  refusal.body = null;
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

  it('deletes a segment only after the confirm, with a DELETE that names the segment, then reads again', async () => {
    await show(SchedulePanel);
    const readsBefore = calls.filter((call) => call.method === 'GET').length;

    await press('[data-segment-delete]');

    expect(sent('DELETE')).toHaveLength(0);
    expect(document.body.textContent).toContain('Delete');

    await press('[data-confirm-action]');

    const removed = sent('DELETE')[0];

    expect(sent('DELETE')).toHaveLength(1);
    expect(removed?.path).toBe('/api/v{version}/channels/{channelId}/live-ops/schedule/segment/{segmentId}');
    expect(removed?.pathParams).toMatchObject({ channelId: 'c1', segmentId: 'seg1' });
    expect(calls.filter((call) => call.method === 'GET').length).toBeGreaterThan(readsBefore);
  });

  it('offers Save vacation only after the vacation changed, and sends the window, then reads again', async () => {
    await show(SchedulePanel);
    const readsBefore = calls.filter((call) => call.method === 'GET').length;

    expect(document.body.querySelector('[data-vacation-save]')).toBeNull();

    await press('[data-slot="setting-row"] button[role="switch"]');
    expect(pick<HTMLButtonElement>('[data-vacation-save]').disabled).toBe(true);

    await typeInto('#vacation-start', '2026-11-01T10:00');
    await typeInto('#vacation-end', '2026-11-08T10:00');
    expect(pick<HTMLButtonElement>('[data-vacation-save]').disabled).toBe(false);

    await press('[data-vacation-save]');

    expect(sent('PUT')[0]?.body).toMatchObject({ isVacationEnabled: true });
    expect(calls.filter((call) => call.method === 'GET').length).toBeGreaterThan(readsBefore);
  });

  // Stoney 2026-10-21 (his phone): the title got one word per line beside Weekly, Edit and Cancel.
  it('gives a segment title its own full-width line below md, with Edit and Cancel in one group under it', async () => {
    await show(SchedulePanel);

    const title = pick<HTMLElement>('[data-segment-title]');
    const actions = pick<HTMLElement>('[data-segment-actions]');

    expect(title.className).toContain('max-md:basis-full');
    expect(actions.contains(pick('[data-segment-edit]'))).toBe(true);
    expect(actions.contains(pick('[data-segment-cancel]'))).toBe(true);
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

  it('shows one translated line next to each field the server refused, and none beside the others', async () => {
    refusal.body = {
      status: 400,
      message: 'The weight for category must be between -100 and 100.',
      code: 'VALIDATION_FAILED',
      data: {
        problems: [
          {
            reason: 'weight-out-of-range',
            field: 'category',
            subject: 'Just Chatting',
            value: 500,
            limit: 100,
          },
          {
            reason: 'weight-out-of-range',
            field: 'followed',
            subject: '',
            value: 900,
            limit: 100,
          },
        ],
      },
    };

    await show(RaidRulesPanel);
    await typeInto('#followed', '900');
    await press('[data-raid-rules-save]');

    const lines = [...document.body.querySelectorAll('[data-raid-problem]')].map((line) => ({
      field: line.getAttribute('data-raid-problem'),
      text: line.textContent?.trim(),
    }));

    expect(lines).toEqual([
      { field: 'category', text: 'Points must stay between -100 and 100. Now: 500.' },
      { field: 'followed', text: 'Points must stay between -100 and 100. Now: 900.' },
    ]);

    expect(pick('[data-category-rule]').nextElementSibling?.getAttribute('data-raid-problem')).toBe('category');
    expect(pick('#followed').parentElement?.querySelector('[data-raid-problem="followed"]')).not.toBeNull();
    expect(pick('#other-weight').parentElement?.querySelector('[data-raid-problem]')).toBeNull();
  });

  // Owner 2026-10-18: "no autocomplete search? we know all the options up front".
  it('picks a preferred category from the Twitch search, not free text', async () => {
    await show(RaidRulesPanel);

    await typeInto('[data-category-rule] input', 'soft');
    await new Promise((done) => setTimeout(done, 400));
    await settle();

    const search = calls.find((call) => call.path.endsWith('/stream/categories'));

    expect(search?.query).toEqual({ query: 'soft' });

    await press('[data-category-rule] [data-category-result]');
    await press('[data-raid-rules-save]');

    expect(sent('PUT')[0]?.body).toEqual({
      ...rules,
      preferredCategories: [{ ...rules.preferredCategories[0], name: 'Software and Game Development' }],
    });
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

describe('what the person may change', () => {
  it('shows the schedule with no vacation Save, Add, Edit or Cancel without the schedule write key', async () => {
    access.held = [];
    await show(SchedulePanel);

    expect(document.body.querySelector('[data-segment-row]')).not.toBeNull();
    expect(document.body.querySelector('[data-vacation-save]')).toBeNull();
    expect(document.body.querySelector('[data-segment-add]')).toBeNull();
    expect(document.body.querySelector('[data-segment-edit]')).toBeNull();
    expect(document.body.querySelector('[data-segment-cancel]')).toBeNull();
    expect(document.body.querySelector('[data-segment-delete]')).toBeNull();
  });

  it('shows the raid rules with no Save, Add or Remove button without the raids write key', async () => {
    access.held = [];
    await show(RaidRulesPanel);

    expect(document.body.querySelector('[data-category-rule]')).not.toBeNull();
    expect(document.body.querySelector('[data-raid-rules-save]')).toBeNull();
    expect(document.body.querySelectorAll('button')).toHaveLength(0);
  });

  it('shows no ad lengths and no Run button without the ads write key', async () => {
    access.held = [];
    await show(CommercialPanel);

    expect(document.body.querySelector('[data-commercial-run]')).toBeNull();
    expect(document.body.querySelector('[data-commercial-length]')).toBeNull();
  });
});
