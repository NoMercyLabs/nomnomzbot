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
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminAccessView from '../AdminAccessView.vue';
import en from '../i18n/en.json';
import { dashboardAdminAccessRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];
const accepted: string[] = [];

const operator = {
  id: 'p1',
  principalType: 0,
  userId: 'u1',
  name: 'Ada Operator',
  isActive: true,
  activeAssignments: [
    {
      id: 'a1',
      principalId: 'p1',
      roleId: 'r1',
      roleName: 'Support',
      createdAt: '2026-10-01T00:00:00Z',
    },
  ],
};

const retired = {
  id: 'p2',
  principalType: 1,
  name: 'Old Robot',
  isActive: false,
  activeAssignments: [],
};

const roles = [
  {
    id: 'r1',
    name: 'Support',
    description: 'Reads tenants',
    isSystem: true,
    permissionKeys: ['tenant:read', 'tenant:access'],
  },
  {
    id: 'r2',
    name: 'Billing',
    description: 'Manages invoices',
    isSystem: false,
    permissionKeys: ['billing:manage'],
  },
];

// The signed-in admin holds every platform key unless a case takes them away.
const keys = vi.hoisted(() => ({ held: true }));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => keys.held }),
}));

vi.mock('@/api/auth', async (original) => ({
  ...(await original<object>()),
  acceptAccessToken: (token: string) => accepted.push(token),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      calls.push({
        method: 'GET',
        path,
        options,
      });

      if (path.endsWith('/iam/roles'))
        return { data: { data: roles }, response: { status: 200, ok: true } };

      return {
        data: { data: [operator, retired] },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (path.endsWith('/impersonate'))
        return {
          data: { data: { accessToken: 'minted-token', sessionId: 's1' } },
          response: { status: 200, ok: true },
        };

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options?: Options) => {
      calls.push({
        method: 'DELETE',
        path,
        options,
      });

      return { data: undefined, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(async () => {
  keys.held = true;
  calls.length = 0;
  accepted.length = 0;
  localStorage.clear();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        name: 'dashboard-home',
        component: { render: () => null },
      },
      {
        path: '/admin/access',
        name: 'access',
        component: { render: () => null },
      },
    ],
  });

  await router.push('/admin/access');
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  queryClient.clear();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminAccessView, {
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

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

async function pick(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLSelectElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('change'));
  await settle();
}

describe('AdminAccessView', () => {
  it('lists the principals with their active roles and the role catalog with permission counts', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-principal-row]')).toHaveLength(2);
    expect(text()).toContain('Ada Operator');
    expect(document.body.querySelector('[data-assignment]')?.textContent).toContain('Support');
    expect(document.body.querySelectorAll('[data-role-row]')).toHaveLength(2);
    expect(text()).toContain('Reads tenants');
    expect(text()).toContain('2 permissions');
  });

  it('remembers the inactive filter across a remount', async () => {
    await open();
    expect(text()).toContain('Old Robot');

    (document.body.querySelector('[data-hide-inactive]') as HTMLInputElement).click();
    await settle();
    expect(text()).not.toContain('Old Robot');
    expect(localStorage.getItem('nnz.adminAccess.hideInactive')).toBe('true');

    mounted?.unmount();
    await open();
    expect(text()).not.toContain('Old Robot');
  });

  it('assigns the picked role with the typed reason', async () => {
    await open();
    (document.body.querySelector('[data-assign]') as HTMLElement).click();
    await settle();
    await pick('[data-field-role]', 'r2');
    await type('[data-field-reason]', 'covers billing week');
    byText('[role="dialog"] button', 'Assign')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/iam/assignments');

    expect(post.options?.body).toMatchObject({
      principalId: 'p1',
      roleId: 'r2',
      reason: 'covers billing week',
    });
  });

  it('lists the principals without assign or revoke when the admin lacks iam:manage', async () => {
    keys.held = false;
    await open();

    expect(document.body.textContent).toContain('Billing');
    expect(document.body.querySelector('[data-assign]')).toBeNull();
    expect(document.body.querySelector('[data-revoke]')).toBeNull();
  });

  it('revokes one assignment with its reason and touches nothing before the confirm', async () => {
    await open();
    (document.body.querySelector('[data-revoke]') as HTMLElement).click();
    await settle();

    expect(calls.some((call) => call.method === 'DELETE')).toBe(false);

    await type('[data-field-reason]', 'rotation ended');
    byText('[role="dialog"] button', 'Revoke role')!.click();
    await settle();

    const removal = calls.find((call) => call.method === 'DELETE')!;

    expect(removal.path).toBe('/api/v{version}/platform/iam/assignments/{assignmentId}');
    expect(removal.options?.params?.path?.assignmentId).toBe('a1');
    expect(removal.options?.params?.query?.reason).toBe('rotation ended');
  });

  it('names what deactivating removes before it posts, then deactivates with the reason', async () => {
    await open();
    (document.body.querySelector('[data-deactivate]') as HTMLElement).click();
    await settle();

    expect(text()).toContain('Ada Operator loses 1 active role');
    expect(calls.some((call) => call.method === 'POST')).toBe(false);

    await type('[data-field-reason]', 'left the team');
    byText('[role="dialog"] button', 'Deactivate principal')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/iam/principals/{principalId}/deactivate');
    expect(post.options?.params?.path?.principalId).toBe('p1');
    expect(post.options?.params?.query?.reason).toBe('left the team');
  });

  it('reactivates an inactive principal', async () => {
    await open();
    (document.body.querySelector('[data-reactivate]') as HTMLElement).click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    expect(post.path).toBe('/api/v{version}/platform/iam/principals/{principalId}/reactivate');
    expect(post.options?.params?.path?.principalId).toBe('p2');
  });

  it('ends a tenant access grant by id', async () => {
    await open();
    await type('[data-field-grant]', 'g1');
    byText('button', 'End access')!.click();
    await settle();

    const removal = calls.find((call) => call.method === 'DELETE')!;

    expect(removal.path).toBe('/api/v{version}/admin/access/{accessGrantId}');
    expect(removal.options?.params?.path?.accessGrantId).toBe('g1');
  });

  it('impersonates through the open grant, signs in as the minted token and goes home', async () => {
    await open();
    await type('[data-field-grant]', 'g1');
    await type('[data-field-user]', 'u9');
    await type('[data-field-justification]', 'ticket 42');
    byText('button', 'Impersonate')!.click();
    await settle();

    const post = calls.find((call) => call.path.endsWith('/impersonate'))!;

    expect(post.options?.params?.path?.userId).toBe('u9');
    expect(post.options?.body).toMatchObject({ accessGrantId: 'g1', justification: 'ticket 42' });
    expect(accepted).toEqual(['minted-token']);
    expect(router.currentRoute.value.name).toBe('dashboard-home');
  });

  it('reloads the page when the hub says another operator changed access', () => {
    const invalidate = vi.fn<(filter: { queryKey: string[] }) => void>();
    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveAccessChanged')!;

    handler.handle(
      { queries: { invalidateQueries: invalidate } as unknown as QueryClient, channelId: null },
      {
        action: 'assigned',
        principalId: 'p1',
        assignmentId: 'a1',
      } as never,
    );

    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['admin', 'access'] });
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-access');

    expect(registered?.path).toBe('admin/access');
    expect(registered?.beforeEnter).toBe(dashboardAdminAccessRoute.beforeEnter);
  });
});
