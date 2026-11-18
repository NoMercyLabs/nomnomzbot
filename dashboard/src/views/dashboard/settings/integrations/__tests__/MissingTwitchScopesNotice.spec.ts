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
const dialogWords = common.common.regrantDialog;
const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

const missing = (...names: string[]) =>
  ok({
    connectionStatus: 'connected',
    scopes: names.map((scope) => ({
      scope,
      features: ['polls'],
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
const dialog = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-slot="regrant-dialog"]');
const action = (name: string): HTMLElement | null =>
  document.body.querySelector<HTMLElement>(`[data-action="${name}"]`);

// Opens the guided dialog and continues to Twitch: the code step.
async function startOnTwitch(): Promise<void> {
  grant()?.click();
  await settle();
  action('regrant-start')?.click();
  await settle();
}

describe('Missing Twitch permissions notice', () => {
  it('shows a short notice with one button and no raw scope names or code', async () => {
    await open();

    expect(notice()?.textContent).not.toContain('channel:read:ads');
    expect(notice()?.textContent).not.toContain('ABCD-EFGH');
    expect(grant()?.textContent?.trim()).toBe(words.grant);
    expect(document.body.querySelectorAll('button')).toHaveLength(1);
    expect(grant()?.getAttribute('data-variant')).toBe('default');
    expect(dialog()).toBeNull();
  });

  it('opens a guided dialog that says in plain words what the bot gains, before anything starts', async () => {
    await open();
    grant()?.click();
    await settle();

    expect(dialog()?.textContent).toContain(dialogWords.title);
    expect(dialog()?.textContent).toContain(dialogWords.features.polls);
    expect(action('regrant-start')?.textContent?.trim()).toBe(dialogWords.start);
    expect(http.POST).not.toHaveBeenCalled();
  });

  it('starts the additive re-grant from the dialog and guides the person through the code step', async () => {
    await open();
    await startOnTwitch();

    expect(http.POST).toHaveBeenCalledTimes(1);
    expect(http.POST.mock.calls[0]?.[0]).toBe(regrantPath);
    expect(dialog()?.querySelector('[data-slot="regrant-code"]')?.textContent).toContain('ABCD-EFGH');
    expect(action('regrant-open-twitch')?.getAttribute('href')).toBe('https://www.twitch.tv/activate');
    expect(action('regrant-start')).toBeNull();
    expect(notice()?.textContent).not.toContain('ABCD-EFGH');
  });

  it('polls the device approval after the code shows, and says it is waiting', async () => {
    vi.useFakeTimers();
    await open();
    await startOnTwitch();
    await vi.advanceTimersByTimeAsync(5000);

    const polled = http.POST.mock.calls.filter(([path]) => path === pollPath);

    expect(polled).toHaveLength(1);
    expect(polled[0]?.[1]).toMatchObject({ body: { deviceCode: 'dev-1' } });
    expect(dialog()?.textContent).toContain(dialogWords.stepApprove);
  });

  it('reads the missing scopes again on approval, says so in the dialog, and Done closes it', async () => {
    vi.useFakeTimers();
    polls = ['authorized'];
    await open();
    await startOnTwitch();
    missingAnswer = missing();
    await vi.advanceTimersByTimeAsync(5000);
    await settle();

    const missingReads = http.GET.mock.calls.filter(([path]) => path === missingPath);

    expect(missingReads).toHaveLength(2);
    expect(dialog()?.querySelector('[data-slot="regrant-code"]')).toBeNull();
    expect(dialog()?.textContent).toContain(dialogWords.doneTitle);

    action('regrant-done')?.click();
    await settle();

    expect(dialog()).toBeNull();
    expect(notice()).toBeNull();
  });

  it.each([
    ['expired', regrantWords.expired],
    ['denied', regrantWords.denied],
  ])('says the code %s and offers to try again with a new code', async (status, text) => {
    vi.useFakeTimers();
    polls = [status];
    await open();
    await startOnTwitch();
    await vi.advanceTimersByTimeAsync(5000);
    await settle();

    expect(dialog()?.querySelector('[data-slot="regrant-status"]')?.textContent).toContain(text);
    expect(dialog()?.querySelector('[data-slot="regrant-code"]')).toBeNull();

    action('regrant-retry')?.click();
    await settle();

    expect(http.POST.mock.calls.filter(([path]) => path === regrantPath)).toHaveLength(2);
  });

  it('shows the error text in the dialog, and offers to try again, when the re-grant cannot start', async () => {
    await open();
    http.POST.mockResolvedValueOnce({ error: {}, response: { ok: false, status: 500 } });
    await startOnTwitch();

    expect(dialog()?.textContent).toContain(dialogWords.failed);
    expect(action('regrant-retry')).not.toBeNull();
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
