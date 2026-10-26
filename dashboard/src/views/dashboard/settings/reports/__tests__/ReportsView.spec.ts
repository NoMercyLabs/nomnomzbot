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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ReportsView from '../ReportsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = {
  method: string;
  path: string;
  body?: unknown;
  query?: unknown;
  reportId?: string;
};

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({ triage: true, fails: false }));
const store = vi.hoisted(() => ({ reports: [] as Record<string, unknown>[] }));

const report = (id: string, status: string, who: string): Record<string, unknown> => ({
  id,
  reportedTwitchUserId: `t-${id}`,
  reportedUsername: who,
  reason: `Reason ${id}`,
  status,
  reporterName: 'Viewer_One',
  createdAt: '2026-10-25T10:00:00Z',
  resolvedAt: null,
  resolvedByName: null,
});

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (
      path: string,
      init: { params: { query: { status: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        query: init.params.query,
      });

      return {
        data: { data: store.reports.filter((row) => row.status === init.params.query.status) },
        response: { ok: true, status: 200 },
      };
    },
    PATCH: async (
      path: string,
      init: { body: { action: string }; params: { path: { reportId: string } } },
    ): Promise<Answer> => {
      calls.push({
        method: 'PATCH',
        path,
        body: init.body,
        reportId: init.params.path.reportId,
      });

      if (access.fails)
        return { error: { detail: 'No' }, response: { ok: false, status: 400 } };

      const status = init.body.action === 'dismiss'
        ? 'dismissed'
        : 'escalated';

      store.reports = store.reports.map((row) =>
        row.id === init.params.path.reportId
          ? { ...row, status }
          : row);

      return { data: { data: {} }, response: { ok: true, status: 200 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:report:triage' || access.triage }),
}));

const toasts = vi.hoisted(() => ({
  success: vi.fn<(text: string) => void>(),
  error: vi.fn<(text: string) => void>(),
}));

vi.mock('vue-sonner', () => ({ toast: toasts }));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(ReportsView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

const ids = (page: VueWrapper): (string | undefined)[] =>
  page.findAll('[data-report]').map((row) => row.attributes('data-report'));

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.triage = true;
  access.fails = false;
  calls.length = 0;
  vi.clearAllMocks();

  store.reports = [
    report('r1', 'open', 'spammer'),
    report('r2', 'open', 'troll'),
    report('r3', 'dismissed', 'oldcase'),
  ];
});

afterEach(() => {
  wrapper?.unmount();
  document.body.innerHTML = '';
});

describe('ReportsView', () => {
  it('asks for the open queue and shows who was reported, why and by whom', async () => {
    const page = await open();

    expect(calls[0]).toMatchObject({ method: 'GET', query: { status: 'open' } });
    expect(ids(page)).toEqual(['r1', 'r2']);
    expect(page.get('[data-report="r1"]').text()).toContain('spammer');
    expect(page.get('[data-report="r1"]').text()).toContain('Reason r1');
    expect(page.get('[data-report="r1"]').text()).toContain('Filed by Viewer_One');
  });

  it('dismisses one report by id and drops it from the open queue', async () => {
    const page = await open();

    await page.get('[data-report-dismiss="r1"]').trigger('click');
    await flushPromises();

    expect(calls.filter((call) => call.method === 'PATCH')).toEqual([
      {
        method: 'PATCH',
        path: '/api/v{version}/channels/{channelId}/moderation/reports/{reportId}',
        body: { action: 'dismiss' },
        reportId: 'r1',
      },
    ]);

    expect(ids(page)).toEqual(['r2']);
    expect(toasts.success).toHaveBeenCalledWith('The report about spammer is dismissed.');
  });

  it('escalates a report with the escalate action', async () => {
    const page = await open();

    await page.get('[data-report-escalate="r2"]').trigger('click');
    await flushPromises();

    expect(calls.find((call) => call.method === 'PATCH')?.body).toEqual({ action: 'escalate' });
    expect(ids(page)).toEqual(['r1']);
  });

  it('keeps the report and says so when the bot refuses', async () => {
    access.fails = true;
    const page = await open();

    await page.get('[data-report-dismiss="r1"]').trigger('click');
    await flushPromises();

    expect(toasts.error).toHaveBeenCalledWith('The report about spammer could not be changed.');
    expect(ids(page)).toEqual(['r1', 'r2']);
  });

  it('shows the dismissed history without any work buttons', async () => {
    const page = await open();

    const dismissed = page
      .findAll('[data-segment]')
      .find((segment) => segment.text() === 'Dismissed');

    await dismissed?.trigger('click');
    await flushPromises();

    expect(ids(page)).toEqual(['r3']);
    expect(page.find('[data-report-dismiss]').exists()).toBe(false);
    expect(page.find('[data-report-escalate]').exists()).toBe(false);
  });

  it('shows the queue without work buttons when triage is not held', async () => {
    access.triage = false;
    const page = await open();

    expect(ids(page)).toEqual(['r1', 'r2']);
    expect(page.find('[data-report-dismiss]').exists()).toBe(false);
  });
});
