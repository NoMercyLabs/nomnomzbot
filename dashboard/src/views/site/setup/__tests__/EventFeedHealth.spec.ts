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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';
import common from '@/i18n/locales/en.json';

import en from '../i18n/en.json';
import EventFeedHealth from '../components/EventFeedHealth.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: vi.fn<() => void>(),
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/api/auth', () => ({
  useSignedIn: () => ref(true),
  acceptAccessToken: vi.fn<(token: string) => void>(),
}));

vi.mock('@/api/channels', () => ({
  myChannelsQuery: {
    queryKey: ['channels', 'mine'],
    queryFn: async () => [{ id: 'c1' }],
  },
}));

const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });
const words = common.common.regrant;
const feed = en.site.setup.feed;
const lacking = { scopes: [{ scope: 'channel:read:ads' }] };
const nothingMissing = { scopes: [] };

let mounted: VueWrapper | null = null;
let missing: object = lacking;
let outcome = 'authorized';

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setInterval', 'setTimeout'] });
  missing = lacking;
  http.GET.mockReset();
  http.POST.mockReset();

  http.GET.mockImplementation(async (path) => {
    if (path.endsWith('/missing-scopes'))
      return ok(missing);

    return path.endsWith('/subscriptions')
      ? ok([{ enabled: true, lastError: null }])
      : ok({});
  });

  http.POST.mockImplementation(async (path) =>
    path.endsWith('/device/poll')
      ? ok({ status: outcome, auth: null })
      : ok({
          deviceCode: 'dev-1',
          userCode: 'ABCD-EFGH',
          verificationUri: 'https://www.twitch.tv/activate',
          interval: 5,
          expiresIn: 600,
          requestedScopes: ['channel:read:ads'],
        }));

  i18n.global.mergeLocaleMessage('en', common);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  vi.useRealTimers();
  mounted?.unmount();
  mounted = null;
});

async function open(): Promise<VueWrapper> {
  mounted = mount(EventFeedHealth, {
    global: {
      plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]],
    },
  });

  await vi.advanceTimersByTimeAsync(0);
  await flushPromises();

  return mounted;
}

async function grant(view: VueWrapper): Promise<void> {
  const button = view.findAll('button').find((item) => item.text().includes(feed.regrant));

  await button?.trigger('click');
  await vi.advanceTimersByTimeAsync(0);
  await flushPromises();
}

describe('Event feed health: the permission re-grant', () => {
  it('shows the code and a waiting line while the approval is open', async () => {
    const view = await open();

    await grant(view);

    expect(view.text()).toContain('ABCD-EFGH');
    expect(view.get('[data-slot="regrant-status"]').text()).toBe(words.waiting);
  });

  it('reads the event feed again on approval and shows the new real state', async () => {
    outcome = 'authorized';

    const view = await open();

    expect(view.text()).toContain(feed.missingScopes);

    await grant(view);
    missing = nothingMissing;
    await vi.advanceTimersByTimeAsync(5000);
    await flushPromises();

    expect(view.text()).not.toContain('ABCD-EFGH');
    expect(view.text()).not.toContain(feed.missingScopes);
    expect(view.text()).toContain('(1 active)');
  });

  it.each([
    ['expired', words.expired],
    ['denied', words.denied],
  ])('says the code %s and offers the grant button again', async (status, text) => {
    outcome = status;

    const view = await open();

    await grant(view);
    await vi.advanceTimersByTimeAsync(5000);
    await flushPromises();

    expect(view.get('[data-slot="regrant-status"]').text()).toBe(text);
    expect(view.text()).not.toContain('ABCD-EFGH');
    expect(view.findAll('button').some((item) => item.text().includes(feed.regrant))).toBe(true);
  });
});
