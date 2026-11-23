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
let undecryptable: boolean = false;

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

// Works: connected, nothing to fix.
const healthy = {
  kind: 'discordGuild',
  provider: 'discord',
  id: 'c-discord',
  accountName: 'Eagle Nest',
  accountId: '9001',
  connected: true,
  status: 'connected',
  needsReauth: false,
  decryptFailed: false,
  missingScopes: [],
  grantedScopes: ['bot', 'applications.commands'],
  tokenExpiresAt: null,
  lastError: null,
  lastErrorAt: null,
  consecutiveFailures: 0,
  lastActivityAt: '2026-11-20T08:00:00Z',
};

// The streamer must sign in to Spotify again.
const reauth = {
  kind: 'integration',
  provider: 'spotify',
  id: 'c-spotify',
  accountName: 'anda_six_music',
  accountId: 'sp-77',
  connected: false,
  status: 'needs_reauth',
  needsReauth: true,
  decryptFailed: false,
  missingScopes: [],
  grantedScopes: [],
  tokenExpiresAt: '2026-11-10T12:00:00Z',
  lastError: 'invalid_grant: refresh token revoked',
  lastErrorAt: '2026-11-19T12:00:00Z',
  consecutiveFailures: 3,
  lastActivityAt: '2026-11-01T00:00:00Z',
};

// Twitch is connected but the grant lacks two scopes the code needs.
const lacking = {
  kind: 'platform',
  provider: 'twitch',
  id: 'c-twitch',
  accountName: 'anda_six',
  accountId: '1',
  connected: true,
  status: 'connected',
  needsReauth: false,
  decryptFailed: false,
  missingScopes: ['channel:manage:broadcast', 'moderator:read:followers'],
  grantedScopes: ['chat:read'],
  tokenExpiresAt: '2026-12-01T00:00:00Z',
  lastError: null,
  lastErrorAt: null,
  consecutiveFailures: 0,
  lastActivityAt: '2026-11-22T09:00:00Z',
};

// The stored token cannot be decrypted, so only a fresh grant helps.
const garbled = {
  kind: 'integration',
  provider: 'youtube',
  id: 'c-youtube',
  accountName: 'anda six',
  accountId: 'yt-5',
  connected: false,
  status: 'decrypt_failed',
  needsReauth: false,
  decryptFailed: true,
  missingScopes: [],
  grantedScopes: [],
  tokenExpiresAt: null,
  lastError: null,
  lastErrorAt: null,
  consecutiveFailures: 0,
  lastActivityAt: null,
};

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
}));

vi.mock('@/api/client', () => {
  const connectionRows = (): Record<string, unknown>[] => {
    if (empty)
      return [];

    if (undecryptable)
      return [garbled];

    return [healthy, reauth, lacking];
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
            connections: connectionRows(),
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
  undecryptable = false;
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
const rows = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-connection-row]')];
const lastRead = (): Call => calls.filter((call) => call.path.endsWith('/diagnostics')).at(-1)!;

describe('Connections tab rows', () => {
  it('asks the diagnostics read for the connections section of this channel', async () => {
    await open('/app/admin/channels/t1/connections');

    expect(lastRead().options.params?.path?.broadcasterId).toBe('t1');
    expect(lastRead().options.params?.query?.sections).toBe('connections');
  });

  it('lists every connection with its provider, family and account', async () => {
    await open('/app/admin/channels/t1/connections');

    expect(rows()).toHaveLength(3);
    expect(rows()[0]!.textContent).toContain('discord');
    expect(rows()[0]!.textContent).toContain('Discord server');
    expect(rows()[0]!.textContent).toContain('Eagle Nest');
    expect(rows()[1]!.textContent).toContain('spotify');
    expect(rows()[1]!.textContent).toContain('Integration');
    expect(rows()[2]!.textContent).toContain('twitch');
    expect(rows()[2]!.textContent).toContain('Platform');
    expect(rows()[2]!.textContent).toContain('anda_six');
  });

  it('shows a working connection as plain words with no status mark', async () => {
    await open('/app/admin/channels/t1/connections');

    const row = rows()[0]!;

    expect(row.textContent).toContain('Connected');
    expect(row.querySelector('[data-slot="status-badge"]')).toBeNull();
    expect(row.querySelector('[data-connection-icon]')).toBeNull();
    expect(row.textContent).toContain('bot');
    expect(row.textContent).toContain('applications.commands');
  });

  it('shows a connection that needs a new sign-in with a word, an icon and the warning tone, plus its last problem', async () => {
    await open('/app/admin/channels/t1/connections');

    const row = rows()[1]!;
    const badge = row.querySelector<HTMLElement>('[data-slot="status-badge"]')!;

    expect(badge.textContent).toContain('Sign in again');
    expect(badge.dataset.tone).toBe('attention');
    expect(row.querySelector('[data-connection-icon]')).not.toBeNull();
    expect(row.textContent).toContain('needs_reauth');
    expect(row.textContent).toContain('invalid_grant: refresh token revoked');
    expect(row.textContent).toContain('3 failures in a row');
    expect(row.textContent).toContain('Nov 10, 2026');
  });

  it('names each missing scope of a connection that lacks permissions', async () => {
    await open('/app/admin/channels/t1/connections');

    const row = rows()[2]!;
    const badge = row.querySelector<HTMLElement>('[data-slot="status-badge"]')!;

    expect(badge.textContent).toContain('Missing scopes');
    expect(badge.dataset.tone).toBe('attention');
    expect(row.textContent).toContain('channel:manage:broadcast');
    expect(row.textContent).toContain('moderator:read:followers');
    expect(row.textContent).toContain('chat:read');
  });

  it('shows a token that cannot be decrypted as a failure, the one red state', async () => {
    undecryptable = true;
    await open('/app/admin/channels/t1/connections');

    const badge = rows()[0]!.querySelector<HTMLElement>('[data-slot="status-badge"]')!;

    expect(rows()).toHaveLength(1);
    expect(badge.textContent).toContain('Cannot decrypt token');
    expect(badge.dataset.tone).toBe('bad');
  });

  it('marks only the connections that need action, never the working one', async () => {
    await open('/app/admin/channels/t1/connections');

    const marked = document.body.querySelectorAll('[data-connection-row] [data-slot="status-badge"]');

    expect(marked).toHaveLength(2);
  });
});

describe('Connections tab states', () => {
  it('shows the empty state when the channel has no connection', async () => {
    empty = true;
    await open('/app/admin/channels/t1/connections');

    expect(rows()).toHaveLength(0);
    expect(text()).toContain('This channel has no connection yet.');
  });

  it('shows one error state with a retry when the read does not load, and the rows after the retry', async () => {
    missing = true;
    await open('/app/admin/channels/t1/connections');

    expect(text()).toContain('The connections could not load.');
    expect(document.body.querySelectorAll('[data-slot="panel-error"]')).toHaveLength(1);
    expect(rows()).toHaveLength(0);

    missing = false;
    byText('button', 'Try again')!.click();
    await settle();

    expect(rows()).toHaveLength(3);
  });
});

describe('Connections tab in the console', () => {
  it('adds a Connections tab after TTS', async () => {
    await open('/app/admin/channels/t1/connections');

    const labels = [...document.body.querySelectorAll('[role="tab"]')].map((tab) =>
      tab.textContent?.trim());

    expect(labels).toEqual([
      'Summary',
      'Runs',
      'TTS',
      'Connections',
      'Billing',
      'People',
      'Limits',
      'Audit',
      'Data',
    ]);
  });

  it('keeps the same text keys in Dutch as in English for the tab', () => {
    const keys = Object.keys(en.dashboard.admin.console.connections);
    const dutchKeys = Object.keys(nl.dashboard.admin.console.connections);

    expect(dutchKeys).toEqual(keys);
  });
});
