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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import MediaShareView from '../MediaShareView.vue';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  body: Record<string, unknown> | undefined;
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let queue: Row[] = [];

const config = {
  isEnabled: true,
  requireApproval: true,
  allowTwitchClips: true,
  allowYouTube: false,
  maxDurationSeconds: 60,
  entryCost: 0,
  maxQueueLength: 10,
  perUserCooldownSeconds: 30,
};

const item = (id: string, status: string) =>
  ({
    id,
    requesterDisplayName: `viewer-${id}`,
    sourceType: 'twitch_clip',
    sourceUrl: `https://clips.example/${id}`,
    title: `Clip ${id}`,
    status,
    requestedAt: '2026-10-01T10:00:00Z',
  }) as const satisfies Row;

const answer = (data: unknown): { data: { data: unknown }; response: { status: number } } => ({
  data: { data },
  response: { status: 200 },
});

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) =>
      path.endsWith('/config')
        ? answer(config)
        : answer(queue),
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return { response: { status: 200 } };
    },
    PUT: async (path: string, options?: Options) => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      return { response: { status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  queue = [item('a', 'pending'), item('b', 'approved'), item('c', 'approved')];
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(MediaShareView, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

// The row attributes sit on the body of the SummaryRow; the action buttons are beside it, so the row is its parent.
const rowOf = (id: string): HTMLElement => {
  const bodies = [...document.body.querySelectorAll<HTMLElement>('[data-media-share-row]')];
  const body = bodies.find((row) => row.textContent?.includes(`Clip ${id}`));

  return body?.parentElement as HTMLElement;
};

const press = async (root: HTMLElement | Document, selector: string): Promise<void> => {
  root.querySelector<HTMLButtonElement>(selector)?.click();
  await flushPromises();
};

describe('Media share page', () => {
  it('approves a pending item with the approve endpoint', async () => {
    await open();
    await press(rowOf('a'), '[data-action="approve"]');

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/media-share/{id}/approve',
        body: undefined,
      },
    ]);
  });

  it('rejects a pending item and skips an approved one', async () => {
    await open();
    await press(rowOf('a'), '[data-action="reject"]');
    await press(rowOf('b'), '[data-action="skip"]');

    expect(calls.map((call) => call.path)).toEqual([
      '/api/v{version}/media-share/{id}/reject',
      '/api/v{version}/media-share/{id}/skip',
    ]);
  });

  it('moves the second approved item up to play position 1', async () => {
    await open();
    await press(rowOf('c'), '[data-action="up"]');

    expect(calls).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/media-share/{id}/reorder',
        body: { position: 1 },
      },
    ]);
  });

  it('offers no move up on the first approved item and no approve on an approved one', async () => {
    await open();

    expect(rowOf('b').querySelector('[data-action="up"]')).toBeNull();
    expect(rowOf('b').querySelector('[data-action="approve"]')).toBeNull();
  });

  it('saves the whole config with the changed value', async () => {
    await open();

    const field = document.body.querySelector<HTMLInputElement>('[data-field="maxQueueLength"]');

    field!.value = '25';
    field!.dispatchEvent(new Event('input'));
    await flushPromises();
    await press(document, '[data-action="save"]');

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/media-share/config',
        body: { ...config, maxQueueLength: 25 },
      },
    ]);
  });

  it('saves subscribers only as the one eligibility rule the bot enforces', async () => {
    await open();

    const rule = document.body.querySelector<HTMLElement>('[data-field="subOnly"] button');

    expect(rule).not.toBeNull();

    rule!.click();
    await flushPromises();
    await press(document, '[data-action="save"]');

    expect(calls).toEqual([
      {
        method: 'PUT',
        path: '/api/v{version}/media-share/config',
        body: { ...config, eligibilityJson: '{"subOnly":true}' },
      },
    ]);
  });
});
