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
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import YourDataView from '../YourDataView.vue';
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
let consents: Body[] = [];
let requests: Body[] = [];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const consent = (type: string, status: string, extra: Body = {}): Body => ({
  id: `id-${type}`,
  broadcasterId: null,
  subjectUserId: 'u1',
  consentType: type,
  status,
  lawfulBasis: 'consent',
  consentVersion: 'v2',
  grantedAt: '2026-10-01T10:00:00Z',
  withdrawnAt: null,
  expiresAt: null,
  ...extra,
});

const request = (id: string, extra: Body = {}): Body => ({
  id,
  subjectUserId: 'u1',
  subjectIdHash: 'h',
  broadcasterId: null,
  requestType: 'export',
  requestedBy: 'self_service',
  status: 'completed',
  scope: 'deployment',
  cryptoShredApplied: false,
  anonymizationApplied: false,
  rowsAffected: 4,
  failureReason: null,
  requestedAt: '2026-10-10T10:00:00Z',
  completedAt: '2026-10-10T10:00:05Z',
  ...extra,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      if (path.endsWith('/erasure/preview')) {
        return ok({
          subjectAlreadyAnonymized: false,
          totalRows: 12,
          categories: [
            { categoryKey: 'gdpr_erasure_category_profile', rowCount: 1 },
            { categoryKey: 'gdpr_erasure_category_chat_messages', rowCount: 11 },
            { categoryKey: 'gdpr_erasure_category_sessions', rowCount: 0 },
          ],
        });
      }

      if (path.endsWith('/gdpr/export')) {
        return ok({
          erasureRequestId: 'r1',
          exportFormat: 'json',
          exportLocation: 'inline',
          sizeBytes: 321,
          rowsAffected: 7,
          generatedAt: '2026-10-18T09:30:00Z',
          document: '{"profile":{"name":"stoney"}}',
        });
      }

      if (path.endsWith('/gdpr/consents'))
        return ok(consents);

      return { data: { data: requests, hasMore: false }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'POST',
        path,
        body: options.body,
        params: options.params,
      });

      return ok(request('new', { status: 'pending' }));
    },
    DELETE: async (path: string, options: { params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        params: options.params,
      });

      return ok({});
    },
  },
}));

const downloads: {
  name: string;
  body: string;
  type: string;
}[] = [];

vi.mock('@/lib/download', () => ({
  downloadText: (name: string, body: string, type: string): void => {
    downloads.push({
      name,
      body,
      type,
    });
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  downloads.length = 0;

  consents = [consent('marketing', 'granted'), consent('leaderboard_opt_in', 'withdrawn')];

  requests = [request('r-done'), request('r-bad', {
    requestType: 'erasure',
    status: 'failed',
    failureReason: 'Key store offline',
  })];

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

  mounted = mount(YourDataView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;
const consentRow = (type: string): HTMLElement => q(`[data-consent="${type}"]`);

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const callTo = (method: string, suffix: string): Call | undefined =>
  calls.find((call) => call.method === method && call.path.endsWith(suffix));

describe('Your data export', () => {
  it('downloads the exported document as a JSON file named for its day', async () => {
    await open();
    await click(q('[data-export]'));

    expect(downloads).toEqual([
      {
        name: 'nomnomzbot-my-data-2026-10-18.json',
        body: '{"profile":{"name":"stoney"}}',
        type: 'application/json',
      },
    ]);

    expect(q('[data-export-result]').textContent).toContain('7 records');
  });
});

describe('Your data consents', () => {
  it('lists each consent with its switch following the ledger status', async () => {
    await open();

    expect(consentRow('marketing').querySelector('button[role="switch"]')?.getAttribute('aria-checked')).toBe('true');

    expect(consentRow('leaderboard_opt_in').querySelector('button[role="switch"]')?.getAttribute('aria-checked')).toBe(
      'false',
    );
  });

  it('withdraws a granted consent with a DELETE for its type', async () => {
    await open();
    await click(consentRow('marketing').querySelector<HTMLElement>('button[role="switch"]') as HTMLElement);

    const sent = callTo('DELETE', '/consents/{consentType}');

    expect(sent?.params).toMatchObject({ path: { consentType: 'marketing' } });
    expect(callTo('POST', '/gdpr/consents')).toBeUndefined();
  });

  it('grants a withdrawn consent again with its own legal basis and version', async () => {
    await open();
    await click(consentRow('leaderboard_opt_in').querySelector<HTMLElement>('button[role="switch"]') as HTMLElement);

    expect(callTo('POST', '/gdpr/consents')?.body).toMatchObject({
      consentType: 'leaderboard_opt_in',
      lawfulBasis: 'consent',
      consentVersion: 'v2',
    });
  });
});

describe('Your data requests', () => {
  it('lists each request with its state and shows why a failed one failed', async () => {
    await open();

    expect(q('[data-request="r-done"]').textContent).toContain(en.dashboard.settings.yourData.requests.status.completed);
    expect(q('[data-request="r-bad"]').textContent).toContain(en.dashboard.settings.yourData.requests.status.failed);
    expect(q('[data-request="r-bad"] [data-failure]').textContent).toContain('Key store offline');
    expect(q('[data-request="r-done"] [data-failure]')).toBeNull();
  });
});

describe('Your data erasure', () => {
  it('counts what it would destroy before it asks and sends nothing until the confirm', async () => {
    await open();
    await click(q('[data-erase]'));

    expect(callTo('GET', '/erasure/preview')).toBeDefined();
    expect(callTo('POST', '/gdpr/erasure')).toBeUndefined();

    const dialog = document.body.textContent ?? '';

    expect(dialog).toContain('11 chat messages');
    expect(dialog).toContain('1 profile');
    expect(dialog).not.toContain('sessions');
  });

  it('requests the erasure after the confirm, and only then', async () => {
    await open();
    await click(q('[data-erase]'));
    await click(q('[data-confirm-action]'));

    const order = calls.map((call) => call.method + call.path.slice(call.path.lastIndexOf('/')));

    expect(order.indexOf('GET/preview')).toBeLessThan(order.indexOf('POST/erasure'));
    expect(q('[data-notice]').textContent).toContain(en.dashboard.settings.yourData.erase.requested);
  });

  it('opts out only after the confirm', async () => {
    await open();
    await click(q('[data-opt-out]'));

    expect(callTo('POST', '/gdpr/opt-out')).toBeUndefined();

    await click(q('[data-confirm-action]'));

    expect(callTo('POST', '/gdpr/opt-out')).toBeDefined();
  });
});

describe('Your data route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-your-data');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-your-data');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();
    expect(router.resolve({ name: 'dashboard-settings-your-data' }).path).toBe('/app/settings/your-data');
    expect(record?.meta).toEqual({ navMode: 'settings', open: true });
    expect(loaded.default).toBe(YourDataView);
  });
});
