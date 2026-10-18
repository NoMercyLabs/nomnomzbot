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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { dashboardRoutes } from '@/router/dashboard';

import AdminScheduledJobsView from '../AdminScheduledJobsView.vue';
import en from '../i18n/en.json';
import { dashboardAdminScheduledJobsRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown>; path?: Record<string, unknown> } };
type Call = {
  method: string;
  path: string;
  query: Record<string, unknown>;
  pathParams: unknown;
};

const calls: Call[] = [];
let jobs: Record<string, unknown>[] = [];
let refused: boolean = false;

const job = (id: string, over: Record<string, unknown> = {}) => ({
  id,
  broadcasterId: 'b1',
  channelDisplayName: 'Stoney_Eagle',
  pipelineId: 'p1',
  pipelineName: 'Voice swap revert',
  pipelineExists: true,
  status: 'fired',
  displayState: 'succeeded',
  dueAt: '2026-10-01T10:00:00Z',
  firedAt: '2026-10-01T10:00:02Z',
  createdAt: '2026-10-01T09:00:00Z',
  triggeredByDisplayName: 'viewer_one',
  canRetry: false,
  ...over,
}) as const satisfies Record<string, unknown>;

async function answer(method: string, path: string, options?: Options) {
  calls.push({
    method,
    path,
    query: { ...options?.params?.query },
    pathParams: options?.params?.path?.taskId,
  });

  if (refused)
    return { error: { message: 'no' }, response: { status: 403, ok: false } };

  if (method === 'POST') {
    return {
      data: {
        data: {
          originalTaskId: 'J2',
          newTaskId: 'J9',
          pipelineId: 'p1',
          pipelineName: 'Voice swap revert',
          newDueAt: '2026-10-01T11:00:00Z',
        },
      },
      response: { status: 200, ok: true },
    };
  }

  return {
    data: {
      data: jobs,
      total: jobs.length,
      hasMore: false,
    },
    response: { status: 200, ok: true },
  };
}

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: (path: string, options?: Options) => answer('GET', path, options),
    POST: (path: string, options?: Options) => answer('POST', path, options),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  refused = false;

  jobs = [
    job('J1'),
    job('J2', {
      status: 'expired',
      displayState: 'failed',
      canRetry: true,
      firedAt: null,
    }),
    job('J3', {
      status: 'expired',
      displayState: 'failed',
      canRetry: false,
      pipelineExists: false,
      pipelineName: null,
    }),
  ];

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

async function open(): Promise<void> {
  mounted = mount(AdminScheduledJobsView, {
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

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-job-row]'),
];
const retryButton = (row: HTMLElement): HTMLButtonElement | null =>
  [...row.querySelectorAll<HTMLButtonElement>('button')].find(
    (node) => node.textContent?.trim() === 'Retry',
  ) ?? null;

describe('AdminScheduledJobsView', () => {
  it('renders one row per job with channel, pipeline, state and trigger', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/jobs');
    expect(rows()).toHaveLength(3);
    expect(rows()[0]!.textContent).toContain('Stoney_Eagle');
    expect(rows()[0]!.textContent).toContain('Voice swap revert');
    expect(rows()[0]!.textContent).toContain('succeeded');
    expect(rows()[0]!.textContent).toContain('viewer_one');
    expect(rows()[1]!.querySelector('[data-tone="bad"]')).not.toBeNull();
  });

  it('retries one failed job by id, then reads the queue again', async () => {
    await open();
    calls.length = 0;

    retryButton(rows()[1]!)!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/admin/jobs/{taskId}/retry');
    expect(post.pathParams).toBe('J2');
    expect(calls.some((call) => call.method === 'GET')).toBe(true);
  });

  it('offers no retry unless the bot says the job can be retried', async () => {
    await open();

    expect(retryButton(rows()[0]!)).toBeNull();
    expect(retryButton(rows()[2]!)).toBeNull();
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see the scheduled jobs.');
    expect(rows()).toHaveLength(0);
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-scheduled-jobs');

    expect(registered?.path).toBe('admin/scheduled-jobs');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminScheduledJobsRoute.beforeEnter);
  });
});
