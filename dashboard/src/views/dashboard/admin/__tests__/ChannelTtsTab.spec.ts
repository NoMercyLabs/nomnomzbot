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
import { createMemoryHistory, createRouter } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminChannelConsoleView from '../AdminChannelConsoleView.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

type Options = {
  params?: { path?: Record<string, string | number>; query?: Record<string, unknown> };
};
type Call = {
  method: string;
  path: string;
  options: Options;
};

const calls: Call[] = [];
let missing: boolean = false;
let empty: boolean = false;
let unmeasured: boolean = false;

const detail = {
  id: 't1',
  name: 'anda_six',
  twitchChannelId: '1',
  status: 'active',
  suspendedReason: null,
  billingTierKey: 'pro',
  deploymentMode: 'saas',
  ownerUserId: 'u1',
  ownerDisplayName: 'Anda_Six',
  membershipCount: 4,
  createdAt: '2026-06-29T10:00:00Z',
};

// A slow clip: 11250 ms is the one the figures and the table must read as 11.3 seconds.
const slowClip = {
  occurredAt: '2026-10-30T10:00:00Z',
  provider: 'azure',
  voiceId: 'en-US-JennyNeural',
  characterCount: 180,
  synthesisMs: 11250,
  failed: false,
};

// A failed clip: the time is what ran until the failure.
const failedClip = {
  occurredAt: '2026-10-30T09:00:00Z',
  provider: 'polly',
  voiceId: 'Joanna',
  characterCount: 60,
  synthesisMs: 7400,
  failed: true,
};

// A clip written before the timing was kept: no synthesis time.
const oldClip = {
  occurredAt: '2026-10-29T09:00:00Z',
  provider: 'azure',
  voiceId: 'en-US-AriaNeural',
  characterCount: 25,
  synthesisMs: null,
  failed: false,
};

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => {
  const ttsSection = (): Record<string, unknown> => {
    if (empty)
      return {
        clips: [],
        last24Hours: {
          clips: 0,
          failedClips: 0,
          averageSynthesisMs: null,
          p95SynthesisMs: null,
        },
      };

    if (unmeasured)
      return {
        clips: [oldClip],
        last24Hours: {
          clips: 1,
          failedClips: 0,
          averageSynthesisMs: null,
          p95SynthesisMs: null,
        },
      };

    return {
      clips: [slowClip, failedClip, oldClip],
      last24Hours: {
        clips: 3,
        failedClips: 1,
        averageSynthesisMs: 9325,
        p95SynthesisMs: 11250,
      },
    };
  };

  const answer = (path: string): Record<string, unknown> => {
    if (path.endsWith('/diagnostics'))
      return {
        data: {
          data: {
            broadcasterId: 't1',
            summary: {
              channelName: 'anda_six',
              tierKey: 'pro',
              tierDisplayName: 'Pro plan',
              isLive: false,
              runsLast24Hours: 2,
              failedRunsLast24Hours: 1,
            },
            tts: ttsSection(),
          },
        },
      };

    if (path.endsWith('/admin/tenants/{broadcasterId}'))
      return { data: { data: detail } };

    return { data: { data: {} } };
  };

  const handle = async (method: string, path: string, options?: Options) => {
    calls.push({
      method,
      path,
      options: options ?? {},
    });

    if (missing && path.endsWith('/diagnostics'))
      // The live box answers an unknown route with a 404 and an empty body, so there is no error object.
      return { response: { status: 404, ok: false } };

    return { ...answer(path), response: { status: 200, ok: true } };
  };

  return {
    client: {
      use: () => undefined,
      GET: (path: string, options?: Options) => handle('GET', path, options),
      POST: (path: string, options?: Options) => handle('POST', path, options),
      PUT: (path: string, options?: Options) => handle('PUT', path, options),
      DELETE: (path: string, options?: Options) => handle('DELETE', path, options),
    },
  };
});

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  missing = false;
  empty = false;
  unmeasured = false;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
  vi.restoreAllMocks();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(start: string): Promise<void> {
  const blank = { template: '<div />' };

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/tenants',
        name: 'dashboard-admin-tenants',
        component: blank,
      },
      {
        path: '/app/admin/channels/:channelId/:tab?',
        name: 'dashboard-admin-channels-console',
        component: AdminChannelConsoleView,
      },
    ],
  });

  await router.push(start);

  mounted = mount(AdminChannelConsoleView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );
const figure = (name: string): string =>
  document.body.querySelector<HTMLElement>(`[data-tts-${name}]`)?.textContent ?? '';
const value = (name: string): string =>
  document.body.querySelector<HTMLElement>(`[data-tts-${name}] [data-tts-value]`)?.textContent?.trim() ?? '';
const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-tts-row]')];
const lastRead = (): Call => calls.filter((call) => call.path.endsWith('/diagnostics')).at(-1)!;

describe('TTS tab figures', () => {
  it('reads the tts section and shows the clips, the failed clips, the average and the 95th percentile of the last 24 hours', async () => {
    await open('/app/admin/channels/t1/tts');

    expect(lastRead().options.params?.path?.broadcasterId).toBe('t1');
    expect(lastRead().options.params?.query?.sections).toBe('tts');
    expect(figure('clips')).toContain('3');
    expect(figure('failed')).toContain('1');
    expect(figure('failed')).toContain('1 of 3 clips failed');
    expect(figure('average')).toContain('9.3 sec');
    expect(figure('p95')).toContain('11.3 sec');
  });

  it('shows a dash for the average and the 95th percentile when no clip has a time', async () => {
    unmeasured = true;
    await open('/app/admin/channels/t1/tts');

    expect(value('average')).toBe('-');
    expect(value('p95')).toBe('-');
  });
});

describe('TTS tab clips', () => {
  it('lists each clip with time, provider, voice, characters and synthesis time', async () => {
    await open('/app/admin/channels/t1/tts');

    const slow = rows()[0]!;

    expect(rows()).toHaveLength(3);
    expect(slow.textContent).toContain('Oct 30, 2026');
    expect(slow.textContent).toContain('azure');
    expect(slow.textContent).toContain('en-US-JennyNeural');
    expect(slow.textContent).toContain('180');
    expect(slow.textContent).toContain('11.3 sec');
  });

  it('marks the failed clip as failed and no other clip', async () => {
    await open('/app/admin/channels/t1/tts');

    const failed = rows()[1]!;
    const marked = document.body.querySelectorAll('[data-tts-row] [data-tone="bad"]');

    expect(failed.textContent).toContain('Failed');
    expect(failed.textContent).toContain('7.4 sec');
    expect(rows()[0]!.textContent).not.toContain('Failed');
    expect(rows()[2]!.textContent).not.toContain('Failed');
    expect(marked).toHaveLength(1);
  });

  it('shows a dash, not 0, for a clip written before the synthesis time was kept', async () => {
    await open('/app/admin/channels/t1/tts');

    const old = rows()[2]!;
    const cell = old.querySelector<HTMLElement>('[data-tts-synthesis]')!;

    expect(old.textContent).toContain('en-US-AriaNeural');
    expect(cell.textContent?.trim()).toBe('-');
  });

  it('shows the empty state for the clips and zero clips in the figures when the channel spoke nothing', async () => {
    empty = true;
    await open('/app/admin/channels/t1/tts');

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('This channel has no TTS clip yet.');
    expect(figure('clips')).toContain('0');
  });

  it('shows one error state with a retry when the read does not load, and the data after the retry', async () => {
    missing = true;
    await open('/app/admin/channels/t1/tts');

    expect(text()).toContain('The TTS clips could not load.');
    expect(document.body.querySelectorAll('[data-slot="panel-error"]')).toHaveLength(1);
    expect(rows()).toHaveLength(0);

    missing = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(rows()).toHaveLength(3);
  });
});

describe('TTS tab in the console', () => {
  it('adds a TTS tab after Runs', async () => {
    await open('/app/admin/channels/t1/tts');

    const labels = [...document.body.querySelectorAll('[role="tab"]')].map((tab) =>
      tab.textContent?.trim());

    expect(labels).toEqual(['Summary', 'Runs', 'TTS', 'Billing', 'People', 'Limits', 'Audit', 'Data']);
  });

  it('keeps the same text keys in Dutch as in English for the tab', () => {
    const keys = Object.keys(en.dashboard.admin.console.tts);
    const dutchKeys = Object.keys(nl.dashboard.admin.console.tts);

    expect(dutchKeys).toEqual(keys);
  });
});
