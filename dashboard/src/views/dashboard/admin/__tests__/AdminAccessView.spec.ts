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
  expiresAt: '2027-03-04T00:00:00Z',
  activeAssignments: [
    {
      id: 'a1',
      principalId: 'p1',
      roleId: 'r1',
      roleName: 'Support',
      createdAt: '2026-10-01T00:00:00Z',
      expiresAt: '2026-12-24T00:00:00Z',
      scopeChannelId: 'chan-scope-1',
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
const keys = vi.hoisted(() => ({
  held: true,
  denied: [] as string[],
  failReactivate: false,
  me: 'someone-else',
  extra: [] as unknown[],
}));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({
    known: { value: true },
    can: (key: string) => keys.held && !keys.denied.includes(key),
  }),
}));

vi.mock('@/api/auth', async (original) => ({
  ...(await original<object>()),
  acceptAccessToken: (token: string) => accepted.push(token),
  useCurrentUser: () => ({ data: { value: { id: keys.me } } }),
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

      if (path.endsWith('/support/people'))
        return {
          data: {
            data: [
              {
                userId: '01HZ0000000000000000000009',
                username: 'viewer9',
                displayName: 'Viewer Nine',
                platform: 'twitch',
                twitchUserId: '909',
                isPlatformPrincipal: false,
                isAnonymized: false,
              },
            ],
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/iam/roles'))
        return { data: { data: roles }, response: { status: 200, ok: true } };

      if (path.endsWith('/{broadcasterId}'))
        return { data: { data: { name: 'Kitte Live' } }, response: { status: 200, ok: true } };

      return {
        data: { data: [operator, retired, ...keys.extra] },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (keys.failReactivate && path.endsWith('/reactivate'))
        return {
          data: undefined,
          error: { title: 'no' },
          response: { status: 500, ok: false },
        };

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
  keys.denied = [];
  keys.failReactivate = false;
  keys.me = 'someone-else';
  keys.extra = [];
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

// The operator finds the person by name; the page fills the id itself.
async function choosePerson(search: string): Promise<void> {
  await type('[data-field-user] [data-person-search]', search);
  document.body.querySelector<HTMLElement>('[data-person-option]')!.click();
  await settle();
}

async function pick(selector: string, label: string): Promise<void> {
  // jsdom lacks these three; the select calls them when it opens.
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;

  document.body.querySelector<HTMLElement>(selector)!.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  await settle();

  const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find(
    (node) => node.textContent?.trim() === label,
  )!;

  option.dispatchEvent(
    new PointerEvent('pointerup', {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    }),
  );

  option.click();
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

    (document.body.querySelector('[data-hide-inactive] [role="switch"]') as HTMLElement).click();
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
    await pick('[data-field-role]', 'Billing');
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
    await type('[data-field-justification]', 'ticket 42');
    await choosePerson('viewer');
    byText('button', 'Impersonate')!.click();
    await settle();

    const post = calls.find((call) => call.path.endsWith('/impersonate'))!;

    expect(post.options?.params?.path?.userId).toBe('01HZ0000000000000000000009');
    expect(post.options?.body).toMatchObject({ accessGrantId: 'g1', justification: 'ticket 42' });
    expect(accepted).toEqual(['minted-token']);
    expect(router.currentRoute.value.name).toBe('dashboard-home');
  });

  it('shows the inactive filter as a switch with its default off', async () => {
    await open();

    const toggle = document.body.querySelector('[data-hide-inactive] [role="switch"]')!;

    expect(toggle.getAttribute('aria-checked')).toBe('false');
    expect(document.body.querySelector('input[type="checkbox"]')).toBeNull();
  });

  it('opens the profile dialog from a principal name that has a user id', async () => {
    await open();

    expect(document.body.querySelector('[data-principal-row] [data-user="u1"]')).not.toBeNull();
    expect(document.body.querySelectorAll('[data-user]')).toHaveLength(1);
  });

  it('shows when a principal and each role assignment expire and which channel it is scoped to', async () => {
    await open();

    const row = document.body.querySelector('[data-principal-row]')!;
    const principalTime = row.querySelector('[data-principal-expires] time');

    expect(principalTime?.getAttribute('datetime')).toBe('2027-03-04T00:00:00.000Z');

    const held = row.querySelector('[data-assignment]')!;
    const heldTime = held.querySelector('[data-assignment-expires] time');

    expect(heldTime?.getAttribute('datetime')).toBe('2026-12-24T00:00:00.000Z');
    expect(held.textContent).not.toContain('chan-scope-1');
    expect(held.querySelector('[data-assignment-scope]')?.textContent).toContain('Kitte Live');
  });

  it('tells two principals with the same name apart by the end of their id', async () => {
    keys.extra = [
      {
        ...operator,
        id: '01KWMJWWS7EGFRBRA4ZW46MGQ8',
        userId: 'u9',
      },
    ];

    await open();

    const tails = [...document.body.querySelectorAll('[data-principal-id]')].map((node) =>
      node.textContent?.trim(),
    );

    expect(tails).toEqual(['ID p1', 'ID p2', 'ID 46MGQ8']);
  });

  it('shows two identical role assignments as one chip with a count', async () => {
    const twin = { ...operator.activeAssignments[0], id: 'a9' };

    keys.extra = [
      {
        ...operator,
        id: 'p3',
        userId: 'u3',
        activeAssignments: [operator.activeAssignments[0], twin],
      },
    ];

    await open();

    const rows = document.body.querySelectorAll('[data-principal-row]');
    const chips = rows[2]!.querySelectorAll('[data-assignment]');

    expect(chips).toHaveLength(1);
    expect(chips[0]!.querySelector('[data-assignment-count]')?.textContent?.trim()).toBe('×2');
  });

  it('keeps Deactivate quiet in the row; only the confirm dialog is filled red', async () => {
    await open();

    const button = document.body.querySelector('[data-deactivate]')!;

    expect(button.className).toContain('text-(--kit-danger)');
    expect(button.className).not.toContain('bg-(--kit-danger)');
  });

  it('says a principal with no active roles holds none, with no zero-plus-plural', async () => {
    keys.extra = [
      {
        ...retired,
        id: 'p4',
        isActive: true,
        name: 'Idle Robot',
      },
    ];

    await open();
    (document.body.querySelectorAll('[data-deactivate]')[1] as HTMLElement).click();
    await settle();

    expect(text()).toContain('Idle Robot holds no active roles');
    expect(text()).not.toContain('loses 0');
  });

  it('warns when the operator revokes a role from their own account', async () => {
    keys.me = 'u1';
    await open();
    (document.body.querySelector('[data-revoke]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[data-self-warning]')?.textContent).toContain(
      'your own account',
    );
  });

  it('does not show the own-account warning for another principal', async () => {
    await open();
    (document.body.querySelector('[data-revoke]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[data-self-warning]')).toBeNull();
  });

  it('assigns with the typed expiry', async () => {
    await open();
    (document.body.querySelector('[data-assign]') as HTMLElement).click();
    await settle();
    await pick('[data-field-role]', 'Billing');
    await type('[data-field-expires]', '2027-01-15T10:30');
    byText('[role="dialog"] button', 'Assign')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;
    const body = post.options?.body as { expiresAt?: string } | undefined;
    const sent = body?.expiresAt ?? '';

    expect(new Date(sent).getTime()).toBe(new Date('2027-01-15T10:30').getTime());
  });

  it('sends no expiry when the field stays empty', async () => {
    await open();
    (document.body.querySelector('[data-assign]') as HTMLElement).click();
    await settle();
    await pick('[data-field-role]', 'Billing');
    byText('[role="dialog"] button', 'Assign')!.click();
    await settle();

    const post = calls.find((call) => call.method === 'POST')!;

    const body = post.options?.body as { expiresAt?: string | null } | undefined;

    expect(body?.expiresAt ?? null).toBeNull();
  });

  it('uses a styled select for the role, not a native one', async () => {
    await open();
    (document.body.querySelector('[data-assign]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('select')).toBeNull();
    expect(document.body.querySelector('[data-field-role][role="combobox"]')).not.toBeNull();
  });

  it('lists the permission keys of a role only after the role row opens', async () => {
    await open();
    expect(text()).not.toContain('billing:manage');

    const rows = document.body.querySelectorAll<HTMLElement>('[data-role-toggle]');

    rows[1]!.click();
    await settle();

    expect(text()).toContain('billing:manage');
    expect(text()).not.toContain('tenant:read');
  });

  it('shows only the end-access tools to an admin who holds tenant:access but not user:impersonate', async () => {
    keys.denied = ['user:impersonate'];
    await open();

    expect(document.body.querySelector('[data-field-grant]')).not.toBeNull();
    expect(byText('button', 'End access')).toBeDefined();
    expect(document.body.querySelector('[data-field-user]')).toBeNull();
    expect(byText('button', 'Impersonate')).toBeUndefined();
  });

  it('shows only the impersonate tools to an admin who holds user:impersonate but not tenant:access', async () => {
    keys.denied = ['tenant:access'];
    await open();

    expect(byText('button', 'End access')).toBeUndefined();
    expect(byText('button', 'Impersonate')).toBeDefined();
  });

  it('hides the whole support panel when neither key is held', async () => {
    keys.denied = ['tenant:access', 'user:impersonate'];
    await open();

    expect(document.body.querySelector('[data-field-grant]')).toBeNull();
  });

  it('keeps Impersonate disabled until a justification is typed', async () => {
    await open();
    await type('[data-field-grant]', 'g1');

    expect(
      document.body.querySelector<HTMLInputElement>('[data-field-user] [data-person-search]')!
        .disabled,
    ).toBe(true);

    expect(text()).toContain('Write the reason first');
    await type('[data-field-justification]', 'ticket 42');
    await choosePerson('viewer');

    expect(text()).toContain('Viewer Nine');
    expect(text()).not.toContain('User id');
    expect((byText('button', 'Impersonate') as HTMLButtonElement).disabled).toBe(false);
  });

  it('tells the operator when a reactivate fails', async () => {
    keys.failReactivate = true;
    await open();
    (document.body.querySelector('[data-reactivate]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[data-reactivate-failure]')?.textContent).toContain(
      'The change failed',
    );
  });

  it('gives Deactivate a danger-colored outline and the revoke chip the 36px height', async () => {
    await open();

    const deactivate = document.body.querySelector('[data-deactivate]')!;
    const assign = document.body.querySelector('[data-assign]')!;

    expect(deactivate.className).toContain('text-(--kit-danger)');
    expect(assign.className).not.toContain('text-(--kit-danger)');
    expect(document.body.querySelector('[data-assignment]')!.className).toContain('h-9');
  });

  it('puts the principal id in the dialog title so same-named principals stay apart', async () => {
    await open();
    (document.body.querySelector('[data-deactivate]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[role="dialog"] h2')?.textContent).toBe(
      'Deactivate Ada Operator (ID p1)',
    );

    byText('[role="dialog"] button', 'Cancel')!.click();
    await settle();
    (document.body.querySelector('[data-revoke]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[role="dialog"] h2')?.textContent).toBe(
      'Revoke Support from Ada Operator (ID p1)',
    );
  });

  it('keeps the Built in badge and the permission count in fixed columns on every role row', async () => {
    await open();

    const rows = [...document.body.querySelectorAll('[data-role-row]')];

    expect(rows).toHaveLength(2);

    for (const row of rows) {
      expect(row.querySelector('[data-role-badge]')!.className).toContain('w-24');
      expect(row.querySelector('[data-role-count]')!.className).toContain('w-28');
    }

    expect(rows[0]!.querySelector('[data-role-badge]')!.textContent).toContain('Built in');
    expect(rows[1]!.querySelector('[data-role-badge]')!.textContent?.trim()).toBe('');
  });

  it('gives the role chips three shares of the row so two chips sit side by side at 1280', async () => {
    await open();

    const chips = document.body.querySelector('[data-principal-row] > div:nth-child(2)')!;

    expect(chips.className).toContain('flex-[3]');
    expect(chips.className).toContain('basis-96');
  });

  it('pads the principals and roles lists to the same 16px as their headers', async () => {
    await open();

    const lists = [
      document.body.querySelector('[data-principal-row]')!.parentElement!,
      document.body.querySelector('[data-role-row]')!.parentElement!,
    ];

    for (const list of lists) {
      expect(list.parentElement!.className).toContain('p-4');
    }
  });

  it('keeps the principal name block a minimum width so wide role chips wrap instead of covering it', async () => {
    await open();

    const nameBlock = document.body.querySelector('[data-principal-row] > div')!;

    expect(nameBlock.className).toContain('min-w-40');
  });

  it('gives the deactivate confirm the destructive look and the assign confirm the lead look', async () => {
    await open();
    (document.body.querySelector('[data-deactivate]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[data-form-action]')!.className).toContain(
      'bg-(--kit-danger)',
    );

    byText('[role="dialog"] button', 'Cancel')!.click();
    await settle();
    (document.body.querySelector('[data-assign]') as HTMLElement).click();
    await settle();

    expect(document.body.querySelector('[data-form-action]')!.className).not.toContain(
      'bg-(--kit-danger)',
    );
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
