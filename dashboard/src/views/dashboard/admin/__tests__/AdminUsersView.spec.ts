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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { dashboardRoutes } from '@/router/dashboard';

import AdminUsersView from '../AdminUsersView.vue';
import en from '../i18n/en.json';
import { dashboardAdminUsersRoute } from '../route';

type Options = { params?: { query?: Record<string, unknown> } };
type Call = { path: string; query: Record<string, unknown> };

const calls: Call[] = [];
let rowsOnPage: Record<string, unknown>[] = [];
let total: number = 0;
let refused: boolean = false;

const person = (
  id: string,
  displayName: string,
  over: Record<string, unknown> = {},
) => ({
  id,
  displayName,
  login: displayName.toLowerCase(),
  email: null,
  role: 'user',
  channelCount: 1,
  createdAt: '2026-06-29T10:00:00Z',
  lastActive: '2026-10-01T10:00:00Z',
  ...over,
}) as const satisfies Record<string, unknown>;

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({ path, query: { ...options?.params?.query } });

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      return {
        data: {
          data: rowsOnPage,
          total,
          hasMore: total > 25,
          nextPage: 2,
        },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  localStorage.clear();
  calls.length = 0;
  refused = false;
  total = 2;

  rowsOnPage = [
    person('u1', 'Stoney_Eagle', {
      role: 'admin',
      channelCount: 3,
      email: 'stoney@example.com',
    }),
    person('u2', 'Quiet_One', { lastActive: null, createdAt: '2026-05-01T10:00:00Z' }),
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

async function open(start: string = '/app/admin/users'): Promise<void> {
  // The frame's sticky footer, where a phone-width page moves its pager.
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/admin/users',
        name: 'dashboard-admin-users',
        component: { template: '<div />' },
      },
    ],
  });

  await router.push(start);

  mounted = mount(AdminUsersView, {
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
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-user-row]'),
];
const lastQuery = (): Record<string, unknown> => calls[calls.length - 1]!.query;
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

describe('AdminUsersView', () => {
  it('opens the user detail with the login, email, role and channel count when a row is clicked', async () => {
    await open();
    rows()[0]!.click();
    await settle();

    const detail = document.body.querySelector('[data-user-detail]') as HTMLElement;

    expect(router.currentRoute.value.query.user).toBe('u1');
    expect(detail.textContent).toContain('Stoney_Eagle');
    expect(detail.textContent).toContain('stoney@example.com');
    expect(detail.textContent).toContain('Admin');
    expect(detail.textContent).toContain('3');
  });

  it('opens the detail of the user named in the address', async () => {
    await open('/app/admin/users?user=u2');

    expect(document.body.querySelector('[data-user-detail]')!.textContent).toContain('Quiet_One');
  });

  it('renders one row per user with name, login, role, channel count and dates', async () => {
    await open();

    expect(calls[0]!.path).toBe('/api/v{version}/admin/users');
    expect(rows()).toHaveLength(2);
    expect(rows()[0]!.textContent).toContain('Stoney_Eagle');
    expect(rows()[0]!.textContent).toContain('stoney_eagle');
    expect(rows()[0]!.textContent).toContain('Admin');
    expect(rows()[0]!.textContent).toContain('3');
    expect(rows()[0]!.textContent).toContain('Oct 1, 2026');
    expect(rows()[1]!.textContent).toContain('Never');
    expect(rows()[1]!.textContent).toContain('May 1, 2026');
    expect(text()).toContain('2 users');
  });

  it('sends the role filter, goes back to page 1 and keeps the choice after a remount', async () => {
    total = 60;
    await open();
    byText('button', 'Next')!.click();
    await settle();
    expect(lastQuery().Page).toBe(2);

    byText('button', 'Admin')!.click();
    await settle();
    expect(lastQuery().role).toBe('admin');
    expect(lastQuery().Page).toBe(1);

    mounted!.unmount();
    mounted = null;
    calls.length = 0;
    await open();
    expect(lastQuery().role).toBe('admin');
  });

  it('refetches with the typed search after the pause', async () => {
    await open();

    const input = document.body.querySelector('input[type="search"]') as HTMLInputElement;

    input.value = 'quiet';
    input.dispatchEvent(new Event('input'));
    await new Promise((done) => setTimeout(done, 400));
    await settle();

    expect(lastQuery().search).toBe('quiet');
  });

  it('shows the forbidden message when the bot refuses the list', async () => {
    refused = true;
    await open();

    expect(text()).toContain('You may not see the user list.');
    expect(rows()).toHaveLength(0);
  });

  it('registers an admin-only route for the users page', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-users');

    expect(registered?.path).toBe('admin/users');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(dashboardAdminUsersRoute.beforeEnter).toBeDefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminUsersRoute.beforeEnter);
  });
});
