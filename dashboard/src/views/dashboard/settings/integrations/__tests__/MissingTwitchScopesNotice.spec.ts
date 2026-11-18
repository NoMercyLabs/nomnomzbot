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
import { computed } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import common from '@/i18n/locales/en.json';

import { answerHeldKeys } from '../../__tests__/heldKeys';
import MissingTwitchScopesNotice from '../components/MissingTwitchScopesNotice.vue';
import en from '../i18n/en.json';
import IntegrationsView from '../IntegrationsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const missingPath = '/api/v{version}/twitch/diagnostics/missing-scopes';
const regrantPath = '/api/v{version}/twitch/diagnostics/regrant';
const pollPath = '/api/v{version}/auth/{provider}/device/poll';
const words = en.dashboard.settings.integrations.missingScopes;
const regrantWords = common.common.regrant;
const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

const missing = (...names: string[]) =>
  ok({
    connectionStatus: 'connected',
    scopes: names.map((scope) => ({
      scope,
      features: ['ads'],
      detectedAtRuntime: false,
      chatNotified: false,
    })),
  });

let mounted: VueWrapper | null = null;
let held: string[] = [];
let missingAnswer: Answer = missing();
let polls: string[] = [];

beforeEach(() => {
  http.GET.mockReset();
  http.POST.mockReset();
  held = ['twitch:diagnostics:read'];
  missingAnswer = missing('channel:read:ads', 'channel:read:charity');

  http.GET.mockImplementation(async (path) =>
    path === missingPath
      ? missingAnswer
      : { error: {}, response: { ok: false, status: 404 } });

  answerHeldKeys(http.GET, () => held);

  polls = [];

  http.POST.mockImplementation(async (path) => {
    if (path === pollPath)
      return ok({ status: polls.shift() ?? 'pending', auth: null });

    return path === regrantPath
      ? ok({
          deviceCode: 'dev-1',
          userCode: 'ABCD-EFGH',
          verificationUri: 'https://www.twitch.tv/activate',
          interval: 5,
          expiresIn: 600,
          requestedScopes: ['channel:read:ads'],
        })
      : { error: {}, response: { ok: false, status: 404 } };
  });

  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', common);
});

afterEach(() => {
  vi.useRealTimers();
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();

  if (vi.isFakeTimers()) {
    await vi.advanceTimersByTimeAsync(0);
  } else {
    await new Promise((done) => setTimeout(done, 0));
  }

  await flushPromises();
}

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(MissingTwitchScopesNotice, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const notice = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-slot="missing-scopes"]');
const grant = (): HTMLButtonElement | null =>
  document.body.querySelector<HTMLButtonElement>('[data-action="grant-scopes"]');

describe('Missing Twitch permissions notice', () => {
  it('lists every missing scope by name and offers one grant button', async () => {
    await open();

    expect(notice()?.textContent).toContain('channel:read:ads');
    expect(notice()?.textContent).toContain('channel:read:charity');
    expect(grant()?.textContent?.trim()).toBe(words.grant);
    expect(document.body.querySelectorAll('button')).toHaveLength(1);
    expect(grant()?.getAttribute('data-variant')).toBe('default');
  });

  it('starts the additive re-grant on click and shows the device code and the verification address', async () => {
    await open();
    grant()?.click();
    await settle();

    expect(http.POST).toHaveBeenCalledTimes(1);
    expect(http.POST.mock.calls[0]?.[0]).toBe(regrantPath);

    const code = document.body.querySelector('[data-slot="missing-scopes-code"]');

    expect(code?.textContent).toContain('ABCD-EFGH');
    expect(code?.querySelector('a')?.getAttribute('href')).toBe('https://www.twitch.tv/activate');
    expect(grant()).toBeNull();
  });

  it('polls the device approval after the code shows, and says it is waiting', async () => {
    vi.useFakeTimers();
    await open();
    grant()?.click();
    await settle();
    await vi.advanceTimersByTimeAsync(5000);

    const polled = http.POST.mock.calls.filter(([path]) => path === pollPath);

    expect(polled).toHaveLength(1);
    expect(polled[0]?.[1]).toMatchObject({ body: { deviceCode: 'dev-1' } });
    expect(document.body.querySelector('[data-slot="regrant-status"]')?.textContent).toContain(regrantWords.waiting);
  });

  it('reads the missing scopes again on approval and replaces the code with the success text', async () => {
    vi.useFakeTimers();
    polls = ['authorized'];
    await open();
    grant()?.click();
    await settle();
    missingAnswer = missing();
    await vi.advanceTimersByTimeAsync(5000);
    await settle();

    const missingReads = http.GET.mock.calls.filter(([path]) => path === missingPath);

    expect(missingReads).toHaveLength(2);
    expect(document.body.querySelector('[data-slot="missing-scopes-code"]')).toBeNull();
    expect(notice()?.textContent).toContain(regrantWords.approved);
  });

  it.each([
    ['expired', regrantWords.expired],
    ['denied', regrantWords.denied],
  ])('says the code %s and offers the grant button again', async (status, text) => {
    vi.useFakeTimers();
    polls = [status];
    await open();
    grant()?.click();
    await settle();
    await vi.advanceTimersByTimeAsync(5000);
    await settle();

    expect(document.body.querySelector('[data-slot="regrant-status"]')?.textContent).toContain(text);
    expect(document.body.querySelector('[data-slot="missing-scopes-code"]')).toBeNull();
    expect(grant()).not.toBeNull();
  });

  it('shows the error text, and keeps the button, when the re-grant fails', async () => {
    await open();
    http.POST.mockResolvedValueOnce({ error: {}, response: { ok: false, status: 500 } });
    grant()?.click();
    await settle();

    expect(notice()?.textContent).toContain(words.regrantFailed);
    expect(grant()).not.toBeNull();
  });

  it('renders nothing when no scope is missing', async () => {
    missingAnswer = missing();
    await open();

    expect(notice()).toBeNull();
  });

  it('renders nothing without the diagnostics key, and never asks for the scopes', async () => {
    held = ['integration:read'];
    await open();

    expect(notice()).toBeNull();
    expect(http.GET.mock.calls.some(([path]) => path === missingPath)).toBe(false);
  });

  it('renders nothing, and the page keeps working, when the scopes cannot be loaded', async () => {
    missingAnswer = { error: {}, response: { ok: false, status: 500 } };
    await open();

    expect(notice()).toBeNull();
  });

  it('sits at the top of the Integrations page, before the services', async () => {
    const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const router = createRouter({ history: createMemoryHistory(), routes: [] });

    mounted = mount(IntegrationsView, {
      attachTo: document.body,
      global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient: queries }]] },
    });

    await settle();

    const page = document.body.querySelector('[data-view="integrations"]');
    const position = notice()?.compareDocumentPosition(page as Node) ?? 0;

    expect(notice()).not.toBeNull();
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
