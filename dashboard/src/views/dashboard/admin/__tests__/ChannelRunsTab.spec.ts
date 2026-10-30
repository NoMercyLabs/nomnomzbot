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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminChannelConsoleView from '../AdminChannelConsoleView.vue';
import en from '../i18n/en.json';

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

const failedRun = {
  id: 101,
  pipelineId: 'pipe-greet',
  triggeredByUserId: 'viewer-9',
  triggerKind: 'command',
  status: 'failed',
  hostCallCount: 2,
  durationMs: 12400,
  errorMessage: 'tts.speak did not answer in time',
  failureKind: 'Timeout',
  limitKey: 'script.timeMs',
  limitValue: 10000,
  startedAt: '2026-10-30T10:00:00Z',
  completedAt: '2026-10-30T10:00:12Z',
};

// A run written before the failure shape: no failure kind, no limit, no host calls.
const oldRun = {
  id: 100,
  pipelineId: 'pipe-hello',
  triggeredByUserId: null,
  triggerKind: 'timer',
  status: 'success',
  hostCallCount: 0,
  durationMs: 300,
  errorMessage: null,
  failureKind: null,
  limitKey: null,
  limitValue: null,
  startedAt: '2026-10-29T10:00:00Z',
  completedAt: '2026-10-29T10:00:01Z',
};

const stepsOfFailedRun = [
  {
    stepIndex: 0,
    actionType: 'play_sound',
    succeeded: true,
    durationMs: 200,
    errorMessage: null,
    hostCalls: [{
      name: 'sound.play',
      durationMs: 180,
      ok: true,
      error: null,
    }],
  },
  {
    stepIndex: 1,
    actionType: 'run_script',
    succeeded: false,
    durationMs: 12200,
    errorMessage: 'Script stopped by its time budget',
    hostCalls: [{
      name: 'tts.speak',
      durationMs: 12000,
      ok: false,
      error: 'no answer from the voice service',
    }],
  },
];

const stepsOfOldRun = [{
  stepIndex: 0,
  actionType: 'send_message',
  succeeded: true,
  durationMs: 300,
  errorMessage: null,
  hostCalls: [],
}];

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => {
  const answer = (path: string, options?: Options): Record<string, unknown> => {
    if (path.endsWith('/diagnostics/runs/{runId}')) {
      const failed = Number(options?.params?.path?.runId) === 101;

      return {
        data: {
          data: {
            ...(failed
              ? failedRun
              : oldRun),
            stepLogs: failed
              ? stepsOfFailedRun
              : stepsOfOldRun,
          },
        },
      };
    }

    if (path.endsWith('/diagnostics')) {
      const failuresOnly = options?.params?.query?.failuresOnly === true;
      const items = empty
        ? []
        : [failedRun, oldRun].filter((run) =>
            !failuresOnly || run.status === 'failed');

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
            runs: {
              items,
              page: Number(options?.params?.query?.Page ?? 1),
              pageSize: 25,
              totalCount: 60,
              hasMore: true,
            },
          },
        },
      };
    }

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

    return { ...answer(path, options), response: { status: 200, ok: true } };
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
let router: Router;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  missing = false;
  empty = false;
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

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/channels',
        name: 'dashboard-admin-channels',
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
const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-run-row]')];
const sheet = (): HTMLElement | null => document.body.querySelector<HTMLElement>('[data-run-detail]');
const lastRead = (): Call => calls.filter((call) => call.path.endsWith('/diagnostics')).at(-1)!;

describe('Runs tab list', () => {
  it('lists the runs of the channel with time, trigger, pipeline, person, status, length, host calls, failure kind and error', async () => {
    await open('/app/admin/channels/t1/runs');

    const failed = rows()[0]!;
    const old = rows()[1]!;
    const read = lastRead();

    expect(read.options.params?.path?.broadcasterId).toBe('t1');
    expect(read.options.params?.query?.sections).toBe('runs');
    expect(rows()).toHaveLength(2);
    expect(failed.textContent).toContain('Oct 30, 2026');
    expect(failed.textContent).toContain('command');
    expect(failed.textContent).toContain('pipe-greet');
    expect(failed.textContent).toContain('viewer-9');
    expect(failed.textContent).toContain('Failed');
    expect(failed.textContent).toContain('12.4 sec');
    expect(failed.textContent).toContain('Timeout');
    expect(failed.textContent).toContain('tts.speak did not answer in time');
    expect(old.textContent).toContain('timer');
    expect(old.textContent).toContain('Succeeded');
    expect(old.textContent).toContain('300 ms');
  });

  it('shows no failure kind for a run written before the failure shape, and no error', async () => {
    await open('/app/admin/channels/t1/runs');

    const old = rows()[1]!;

    expect(old.querySelector('[data-run-failure-kind]')).toBeNull();
    expect(old.querySelector('[role="alert"]')).toBeNull();
    expect(text()).not.toContain('could not load');
  });

  it('puts failuresOnly in the address and in the request when Failures only is pressed', async () => {
    await open('/app/admin/channels/t1/runs');

    byText('[data-segment]', 'Failures only')!.click();
    await settle();

    expect(router.currentRoute.value.query.failuresOnly).toBe('true');
    expect(lastRead().options.params?.query?.failuresOnly).toBe(true);
    expect(rows()).toHaveLength(1);
    expect(rows()[0]!.textContent).toContain('pipe-greet');
  });

  it('starts on the filter named in the address, so a copied link reopens the same view', async () => {
    await open('/app/admin/channels/t1/runs?failuresOnly=true&pipeline=pipe-greet&page=2');

    const query = lastRead().options.params?.query;

    expect(query?.failuresOnly).toBe(true);
    expect(query?.pipelineId).toBe('pipe-greet');
    expect(query?.Page).toBe(2);
    expect(text()).toContain('Page 2 of 3');
  });

  it('puts the pipeline in the address and in the request when one is typed', async () => {
    await open('/app/admin/channels/t1/runs');

    const field = document.body.querySelector<HTMLInputElement>('[data-run-pipeline]')!;

    field.value = 'pipe-hello';
    field.dispatchEvent(new Event('input'));
    await settle();
    field.dispatchEvent(new Event('change'));
    await settle();

    expect(router.currentRoute.value.query.pipeline).toBe('pipe-hello');
    expect(lastRead().options.params?.query?.pipelineId).toBe('pipe-hello');
  });

  it('moves to the next page and writes the page in the address', async () => {
    await open('/app/admin/channels/t1/runs');
    byText('button', 'Next')!.click();
    await settle();

    expect(router.currentRoute.value.query.page).toBe('2');
    expect(lastRead().options.params?.query?.Page).toBe(2);
    expect(text()).toContain('Page 2 of 3');
  });

  it('says so when the channel has no run for the filters', async () => {
    empty = true;
    await open('/app/admin/channels/t1/runs');

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('No run matches these filters.');
  });

  it('shows the error state with a retry when the runs do not load', async () => {
    missing = true;
    await open('/app/admin/channels/t1/runs');

    expect(text()).toContain('The runs could not load.');

    missing = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(rows()).toHaveLength(2);
  });
});

describe('Run detail sheet', () => {
  it('opens from a row and writes the run id in the address', async () => {
    await open('/app/admin/channels/t1/runs');
    rows()[0]!.click();
    await settle();

    expect(router.currentRoute.value.query.run).toBe('101');
    expect(sheet()).not.toBeNull();
    expect(sheet()!.textContent).toContain('pipe-greet');
    expect(sheet()!.textContent).toContain('Failed');

    const read = calls.find((call) => call.path.endsWith('/diagnostics/runs/{runId}'))!;

    expect(read.options.params?.path?.runId).toBe(101);
    expect(read.options.params?.path?.broadcasterId).toBe('t1');
  });

  it('opens on a fresh load of an address that names a run', async () => {
    await open('/app/admin/channels/t1/runs?run=101');

    expect(sheet()).not.toBeNull();
    expect(sheet()!.textContent).toContain('command');
    expect(sheet()!.textContent).toContain('viewer-9');
  });

  it('closes and takes the run id out of the address', async () => {
    await open('/app/admin/channels/t1/runs?run=101');
    document.body.querySelector<HTMLElement>('[data-run-close]')!.click();
    await settle();

    expect(router.currentRoute.value.query.run).toBeUndefined();
    expect(sheet()).toBeNull();
  });

  it('says which limit stopped the run and shows the failure kind', async () => {
    await open('/app/admin/channels/t1/runs?run=101');

    expect(sheet()!.textContent).toContain('Hit limit script.timeMs at 10000');
    expect(sheet()!.querySelector('[data-run-failure-kind]')!.textContent).toContain('Timeout');
  });

  it('lists each step with its error, and inside a step its host calls with their length', async () => {
    await open('/app/admin/channels/t1/runs?run=101');

    const steps = [...sheet()!.querySelectorAll<HTMLElement>('[data-run-step]')];
    const calls = [...sheet()!.querySelectorAll<HTMLElement>('[data-host-call]')];

    expect(steps).toHaveLength(2);
    expect(steps[0]!.textContent).toContain('play_sound');
    expect(steps[1]!.textContent).toContain('run_script');
    expect(steps[1]!.textContent).toContain('Script stopped by its time budget');
    expect(calls).toHaveLength(2);
    expect(steps[1]!.querySelector('[data-host-call]')!.textContent).toContain('tts.speak');
    expect(steps[1]!.querySelector('[data-host-call]')!.textContent).toContain('12 sec');
    expect(steps[1]!.querySelector('[data-host-call]')!.textContent).toContain('no answer from the voice service');
  });

  it('shows a run without host calls as plain steps, with no failure kind and no host call list', async () => {
    await open('/app/admin/channels/t1/runs?run=100');

    expect(sheet()!.querySelectorAll('[data-run-step]')).toHaveLength(1);
    expect(sheet()!.querySelector('[data-host-call]')).toBeNull();
    expect(sheet()!.querySelector('[data-host-calls]')).toBeNull();
    expect(sheet()!.querySelector('[data-run-failure-kind]')).toBeNull();
    expect(sheet()!.textContent).not.toContain('Hit limit');
    expect(sheet()!.textContent).not.toContain('could not load');
  });
});

describe('Runs tab in the console', () => {
  it('drops the run, page and filters from the address when another tab is picked', async () => {
    await open('/app/admin/channels/t1/runs?run=101&page=2&failuresOnly=true&pipeline=pipe-greet');

    byText('[role="tab"]', 'Audit')!.dispatchEvent(
      new MouseEvent('mousedown', { button: 0, bubbles: true }),
    );

    await settle();

    expect(router.currentRoute.value.params.tab).toBe('audit');
    expect(router.currentRoute.value.query).toEqual({});
  });
});
