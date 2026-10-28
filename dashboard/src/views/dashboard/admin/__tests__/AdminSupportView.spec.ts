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
import { dashboardRoutes } from '@/router/dashboard';

import AdminSupportView from '../AdminSupportView.vue';
import en from '../i18n/en.json';
import { dashboardAdminSupportRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };

const calls: Call[] = [];
let refused: boolean = false;

const person = {
  userId: 'u1',
  username: 'stoney_eagle',
  displayName: 'Stoney_Eagle',
  platform: 'twitch',
  twitchUserId: '123',
  isPlatformPrincipal: false,
  isAnonymized: false,
  lastSeenAt: '2026-10-01T10:00:00Z',
  tenantCount: 2,
};

const view = {
  ...person,
  identities: [],
  platformConnections: [
    {
      broadcasterId: 'c1',
      channelName: 'Quiet_One',
      provider: 'twitch',
      externalChannelId: '9',
      displayName: 'Quiet_One',
      isPrimary: true,
      isLive: false,
    },
  ],
  iamRoles: [],
  communityStandings: [
    {
      broadcasterId: 'c1',
      channelName: 'Quiet_One',
      standing: 'moderator',
      levelValue: 5,
      source: 'twitch',
      subTier: null,
      lastSeenAt: null,
    },
  ],
  moderationStandings: [],
  moderationHistory: [],
  trustScores: [],
  entitlements: [],
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({ path, query: { ...options?.params?.query } });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      let data: unknown = { data: [], total: 0 };

      if (path.endsWith('/support/people')) {
        data = { data: [person], total: 1 };
      }

      if (path.endsWith('/support/people/{subjectUserId}')) {
        data = { status: 'success', data: view };
      }

      if (path.endsWith('/history')) {
        data = {
          data: [
            {
              eventId: 'e1',
              broadcasterId: 'c1',
              channelName: 'Quiet_One',
              eventType: 'channel.follow',
              source: 'twitch',
              occurredAt: '2026-10-02T10:00:00Z',
            },
          ],
          total: 1,
        };
      }

      if (path.endsWith('/commands')) {
        data = {
          data: [
            {
              id: 'k1',
              name: 'hug',
              isEnabled: true,
              useCount: 3,
            },
          ],
          total: 1,
        };
      }

      if (path.endsWith('/pipelines')) {
        data = {
          data: [
            {
              id: 'p1',
              name: 'Raid welcome',
              isEnabled: true,
              triggerCount: 4,
            },
          ],
          total: 1,
        };
      }

      return { data, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  refused = false;
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
  await new Promise((done) => setTimeout(done, 350));
  await flushPromises();
};

async function open(personId = ''): Promise<void> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/support',
        name: 'dashboard-admin-support',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(personId
    ? `/app/admin/support?person=${personId}`
    : '/app/admin/support');

  mounted = mount(AdminSupportView, {
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
const sent = (suffix: string): Call | undefined => calls.find((call) => call.path.endsWith(suffix));

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

describe('AdminSupportView', () => {
  it('reads nothing until a justification is given, and says why', async () => {
    await open();
    await type('input[type="search"]', 'stoney');

    expect(calls).toHaveLength(0);
    expect(text()).toContain('Give a reason first. Every lookup is kept in the audit log.');
    expect(text()).toContain('earn money with NomNomzBot');
  });

  it('searches people with the justification and lists the match', async () => {
    await open();
    await type('[data-justification]', 'Ticket 41');
    await type('input[type="search"]', 'stoney');

    const call = sent('/support/people')!;

    expect(call.query.justification).toBe('Ticket 41');
    expect(call.query.search).toBe('stoney');
    expect(document.body.querySelectorAll('[data-person-row]')).toHaveLength(1);
    expect(text()).toContain('Stoney_Eagle');
  });

  it('shows standings and history of the picked person, each read with the justification', async () => {
    await open();
    await type('[data-justification]', 'Ticket 41');
    await type('input[type="search"]', 'stoney');
    document.body.querySelector<HTMLElement>('[data-person-row] button')!.click();
    await settle();

    expect(sent('/support/people/{subjectUserId}')!.query.justification).toBe('Ticket 41');
    expect(sent('/history')!.query.justification).toBe('Ticket 41');
    expect(document.body.querySelector('[data-standing-row]')!.textContent).toContain('moderator');

    expect(document.body.querySelector('[data-history-row]')!.textContent).toContain(
      'channel.follow',
    );
  });

  it('lists the commands then the pipelines of the picked channel', async () => {
    await open('u1');
    await type('[data-justification]', 'Ticket 41');

    expect(sent('/commands')!.query.justification).toBe('Ticket 41');
    expect(document.body.querySelector('[data-command-row]')!.textContent).toContain('hug');

    byText('button', 'Pipelines')!.click();
    await settle();

    expect(document.body.querySelector('[data-pipeline-row]')!.textContent).toContain(
      'Raid welcome',
    );
  });

  it('shows the forbidden message when the bot refuses', async () => {
    refused = true;
    await open();
    await type('[data-justification]', 'Ticket 41');
    await type('input[type="search"]', 'stoney');

    expect(text()).toContain('You may not use the support desk.');
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-support');

    expect(registered?.path).toBe('admin/support');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminSupportRoute.beforeEnter);
  });
});
