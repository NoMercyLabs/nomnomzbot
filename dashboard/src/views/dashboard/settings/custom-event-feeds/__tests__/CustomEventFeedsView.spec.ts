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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import CustomEventFeedsView from '../CustomEventFeedsView.vue';
import en from '../i18n/en.json';

type Body = Record<string, unknown>;
type Call = {
  method: string;
  path: string;
  body?: Body;
  params?: Body;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let feeds: Body[] = [];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const feed = (id: string, name: string, extra: Body = {}): Body => ({
  id,
  name,
  displayName: `Feed ${name}`,
  sourceKind: 'poll',
  presetKey: null,
  endpointUrl: `https://example.test/${name}`,
  hasAuthSecret: true,
  fieldMap: { bpm: '$.heart.bpm' },
  pollIntervalSeconds: 15,
  isEnabled: false,
  lastReceivedAt: null,
  fieldErrors: {},
  inboundUrl: null,
  ...extra,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      if (path.endsWith('/blast-radius')) {
        return ok({
          categories: [{
            categoryKey: 'event_responses',
            count: 3,
            sample: [],
          }],
          isMinimum: false,
          totalReferences: 3,
        });
      }

      if (path.endsWith('/presets'))
        return ok([]);

      return { data: { data: feeds, hasMore: false }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options.body,
        params: options.params,
      });

      return ok(feed('f9', 'made'));
    },
    PUT: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: options.body,
        params: options.params,
      });

      return ok(feed('f1', 'heartrate'));
    },
    DELETE: async (path: string, options: { params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        params: options.params,
      });

      return { response: { status: 204, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;

  feeds = [
    feed('f1', 'heartrate', { isEnabled: false, lastReceivedAt: '2026-10-18T10:00:00Z' }),
    feed('f2', 'ticker', {
      isEnabled: true,
      sourceKind: 'push',
      inboundUrl: 'https://bot.test/in/f2',
    }),
  ];

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(CustomEventFeedsView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;
const row = (name: string): HTMLElement => q(`[data-feed="${name}"]`);

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

const callTo = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

describe('Custom event feeds', () => {
  it('lists each feed with the event it makes, its kind, its last datum and its switch', async () => {
    await open();

    expect(row('heartrate').textContent).toContain('Feed heartrate');
    expect(row('heartrate').textContent).toContain('custom.heartrate');

    expect(row('heartrate').querySelector('[data-last-received]')?.textContent).not.toContain(
      en.dashboard.settings.customEventFeeds.neverReceived,
    );

    expect(row('ticker').querySelector('[data-last-received]')?.textContent).toContain(
      en.dashboard.settings.customEventFeeds.neverReceived,
    );

    expect(row('heartrate').querySelector('button[role="switch"]')?.getAttribute('aria-checked')).toBe('false');
    expect(row('ticker').querySelector('button[role="switch"]')?.getAttribute('aria-checked')).toBe('true');
  });

  it('makes a feed with the typed names, the default poll kind and the field rows', async () => {
    await open();
    await click(q('[data-new-feed]'));
    await type(q<HTMLInputElement>('[data-field="display-name"]'), 'Heart rate');
    await type(q<HTMLInputElement>('[data-field="name"]'), 'pulse');

    expect(q('[data-dialog="event-feed"]').textContent).toContain('{{custom.pulse.field}}');

    await type(q<HTMLInputElement>('[data-field="address"]'), 'https://example.test/pulse');
    await click(q('[data-field-add]'));
    await type(q<HTMLInputElement>('[data-field-name]'), 'bpm');
    await type(q<HTMLInputElement>('[data-field-path]'), '$.hr');
    await click(q('[data-form-action]'));

    expect(callTo('POST', '/custom-data-sources')?.body).toEqual({
      name: 'pulse',
      displayName: 'Heart rate',
      sourceKind: 'poll',
      presetKey: null,
      endpointUrl: 'https://example.test/pulse',
      authSecret: null,
      fieldMap: { bpm: '$.hr' },
      pollIntervalSeconds: 30,
      isEnabled: false,
    });
  });

  it('flips the switch with a PUT of the whole feed, enabled flipped and the secret left empty', async () => {
    await open();
    await click(row('heartrate').querySelector<HTMLElement>('button[role="switch"]') as HTMLElement);

    const sent = callTo('PUT', '/custom-data-sources/{id}');

    expect(sent?.params).toMatchObject({ path: { id: 'f1' } });

    expect(sent?.body).toEqual({
      name: 'heartrate',
      displayName: 'Feed heartrate',
      sourceKind: 'poll',
      presetKey: null,
      endpointUrl: 'https://example.test/heartrate',
      authSecret: null,
      fieldMap: { bpm: '$.heart.bpm' },
      pollIntervalSeconds: 15,
      isEnabled: true,
    });
  });

  it('counts what the feed reaches before it asks, and deletes only after the confirm', async () => {
    await open();
    await click(row('heartrate').querySelector<HTMLElement>('[data-feed-delete]') as HTMLElement);

    expect(callTo('GET', '/blast-radius')).toBeDefined();
    expect(callTo('DELETE', '/custom-data-sources/{id}')).toBeUndefined();
    expect(document.body.textContent).toContain('3');

    await click(q('[data-confirm-action]'));

    expect(callTo('DELETE', '/custom-data-sources/{id}')?.params).toMatchObject({ path: { id: 'f1' } });

    const order = calls.map((call) => call.method + call.path.slice(call.path.lastIndexOf('/')));

    expect(order.indexOf('GET/blast-radius')).toBeLessThan(order.indexOf('DELETE/{id}'));
  });

  it('sends the sample payload the streamer typed to the feed test', async () => {
    await open();
    await click(row('heartrate').querySelector<HTMLElement>('[data-feed-test]') as HTMLElement);
    await type(q<HTMLTextAreaElement>('[data-field="payload"]'), '{"heart":{"bpm":88}}');
    await click(q('[data-form-action]'));

    const sent = callTo('POST', '/{id}/test');

    expect(sent?.params).toMatchObject({ path: { id: 'f1' } });
    expect(sent?.body).toEqual({ samplePayload: '{"heart":{"bpm":88}}' });
    expect(q('[data-test-sent]')).not.toBeNull();
  });
});

describe('Custom event feeds route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-custom-event-feeds');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-custom-event-feeds');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();

    expect(router.resolve({ name: 'dashboard-settings-custom-event-feeds' }).path).toBe(
      '/app/settings/custom-event-feeds',
    );

    expect(record?.meta).toEqual({ navMode: 'settings' });
    expect(loaded.default).toBe(CustomEventFeedsView);
  });
});
