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

import { VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, type Component } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import adminEn from '@/views/dashboard/admin/i18n/en.json';

// The act-as token names the admin in its `act` claim, the way the bot mints it.
function jwt(claims: Record<string, string>): string {
  const json = JSON.stringify(claims);
  const body = btoa(json).replaceAll('=', '').replaceAll('+', '-').replaceAll('/', '_');

  return `e30.${body}.signature`;
}

const adminToken = 'admin-token';
const actToken = jwt({ sub: 'u-anda', act: 'u-stoney' });

const stoney = {
  id: 'u-stoney',
  username: 'stoney_eagle',
  displayName: 'Stoney_Eagle',
  profileImageUrl: 'https://cdn.example/stoney.png',
  broadcasterType: 'partner',
  isAdmin: true,
  createdAt: '2020-01-01T00:00:00Z',
  platformPermissions: [
    'tenant:read',
    'tenant:access',
    'user:impersonate',
  ],
};

const anda = {
  id: 'u-anda',
  username: 'anda_six',
  displayName: 'anda_six',
  profileImageUrl: 'https://cdn.example/anda.png',
  broadcasterType: 'affiliate',
  isAdmin: false,
  createdAt: '2021-01-01T00:00:00Z',
  platformPermissions: [],
};

const tenant = {
  id: 'T1',
  name: 'anda_six',
  twitchChannelId: '600',
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-01-01T00:00:00Z',
};

// The bot as the browser sees it: who a request is from follows its bearer token, and the act-as cookie
// (`acting`) decides what a refresh hands back.
const server = {
  acting: false,
  refusal: null as null | {
    status: number;
    code: string;
    message: string;
  },
  seen: [] as string[],
  bodies: new Map<string, string>(),
};

function json(body: object, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function answer(method: string, path: string, bearer: string): Response {
  if (path === '/api/v1/auth/me')
    return json({ data: bearer === `Bearer ${actToken}`
      ? anda
      : stoney });

  if (path === '/api/v1/auth/refresh')
    return json({ data: { accessToken: server.acting
      ? actToken
      : adminToken } });

  if (path === '/api/v1/auth/impersonation/exit') {
    server.acting = false;

    return json({ data: { accessToken: adminToken } });
  }

  if (path === '/api/v1/admin/tenants')
    return json({ data: [tenant] });

  if (path === '/api/v1/admin/tenants/T1')
    return json({ data: {
      ...tenant,
      ownerDisplayName: 'anda_six',
      deploymentMode: 'selfhost',
    } });

  if (path === '/api/v1/admin/tenants/T1/members')
    return json({
      data: [
        {
          userId: 'u-mod',
          username: 'modman',
          displayName: 'ModMan',
          relation: 'manager',
        },
        {
          userId: 'u-anda',
          username: 'anda_six',
          displayName: 'anda_six',
          relation: 'owner',
        },
      ],
    });

  if (method === 'POST' && path === '/api/v1/admin/tenants/T1/access')
    return json({
      data: {
        id: 'G1',
        principalId: 'P1',
        targetBroadcasterId: 'T1',
        justification: 'debug the failures',
        breakGlass: false,
        grantedAt: '2026-10-30T12:00:00Z',
      },
    });

  if (method === 'POST' && path === '/api/v1/admin/users/u-anda/impersonate') {
    if (server.refusal !== null)
      return json({
        status: 'error',
        message: server.refusal.message,
        code: server.refusal.code,
      }, server.refusal.status);

    server.acting = true;

    return json({ data: {
      accessToken: actToken,
      expiresAt: '2026-10-30T13:00:00Z',
      sessionId: 'G1',
      user: anda,
    } });
  }

  return json({});
}

function stubServer(): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const bearer = request.headers.get('Authorization') ?? 'none';
      const path = new URL(request.url).pathname;
      const line = `${request.method} ${path} ${bearer}`;

      server.seen.push(line);

      if (request.method === 'POST') {
        server.bodies.set(path, await request.clone().text());
      }

      return answer(request.method, path, bearer);
    }),
  );
}

let mounted: VueWrapper | null = null;
let router: Router;

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

// A fresh page load: every module starts over, the way a reload does; the server and storage stay.
async function load(fragment = ''): Promise<void> {
  mounted?.unmount();
  document.body.innerHTML = '';
  vi.resetModules();
  history.replaceState(null, '', `/${fragment}`);

  const auth = await import('@/api/auth');
  const { queryClient } = await import('@/api/queryClient');
  const { i18n } = await import('@/i18n');
  const ActAsExit: Component = (await import('../ActAsExit.vue')).default;
  const ProfileMenu: Component = (await import('../ProfileMenu.vue')).default;
  const AdminChannelsView: Component = (await import('@/views/dashboard/admin/AdminChannelsView.vue')).default;
  const AdminAccessView: Component = (await import('@/views/dashboard/admin/AdminAccessView.vue')).default;

  i18n.global.mergeLocaleMessage('en', adminEn);
  queryClient.setDefaultOptions({ queries: { retry: false } });
  await auth.restoreSession();

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/',
        name: 'dashboard-home',
        component: { render: () => h('p', 'home') },
      },
      {
        path: '/admin/channels',
        name: 'dashboard-admin-channels',
        component: AdminChannelsView,
      },
      {
        path: '/admin/channels/:channelId/:tab?',
        name: 'dashboard-admin-channels-console',
        component: { render: () => null },
      },
      {
        path: '/admin/access',
        name: 'dashboard-admin-access',
        component: AdminAccessView,
      },
    ],
  });

  // The frame: the shell's exit and profile, and the page.
  const frame = defineComponent({
    render: () =>
      h('div', [
        h(ActAsExit),
        h(ProfileMenu),
        h(RouterView),
      ]),
  });

  mounted = mount(frame, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        createPinia(),
        router,
        [VueQueryPlugin, { queryClient }],
      ],
    },
  });

  await router.push(server.acting
    ? '/'
    : '/admin/channels');

  await settle();
}

const html = (): string => document.body.innerHTML.toLowerCase();
const text = (): string => document.body.textContent ?? '';
const dialog = (): HTMLElement => document.body.querySelector<HTMLElement>('[role="dialog"]')!;
const exits = (): NodeListOf<HTMLElement> => document.body.querySelectorAll<HTMLElement>('[data-slot="act-as-exit"]');

function byText(root: ParentNode, selector: string, label: string): HTMLElement {
  return [...root.querySelectorAll<HTMLElement>(selector)].find((node) =>
    node.textContent?.trim() === label)!;
}

async function type(selector: string, value: string): Promise<void> {
  const field = document.body.querySelector<HTMLInputElement>(selector)!;

  field.value = value;
  field.dispatchEvent(new Event('input'));
  await settle();
}

// The admin opens Act as on the channel row, writes the reason, keeps the owner and confirms.
async function actFromRow(): Promise<void> {
  document.body.querySelector<HTMLElement>('[data-channel-row] [data-act-as]')!.click();
  await settle();
  await type('[data-act-as-reason]', 'debug the failures');
  byText(dialog(), '[data-segment]', '30 minutes').click();
  await settle();
  byText(dialog(), 'button', 'Act as').click();
  await settle();
}

beforeEach(() => {
  server.acting = false;
  server.refusal = null;
  server.seen.length = 0;
  server.bodies.clear();
  localStorage.clear();
  stubServer();
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

describe('acting as a channel person from the admin panel', () => {
  it('opens one dialog from the channel row with the owner picked and no grant id field', async () => {
    await load('#access_token=admin-token');
    document.body.querySelector<HTMLElement>('[data-channel-row] [data-act-as]')!.click();
    await settle();

    expect(dialog().textContent).toContain('Act as someone in anda_six');
    expect(dialog().querySelector('[data-act-as-person]')!.textContent).toContain('anda_six');
    expect(dialog().querySelector('[data-field-grant]')).toBeNull();
    expect(dialog().textContent).not.toContain('grant');
  });

  it('opens the session and acts as the owner in one confirm, then shows only the target and one Exit', async () => {
    await load('#access_token=admin-token');

    expect(text()).toContain('Stoney_Eagle');
    expect(exits()).toHaveLength(0);

    const before = Date.now();

    await actFromRow();

    const access = JSON.parse(server.bodies.get('/api/v1/admin/tenants/T1/access')!) as {
      justification: string;
      breakGlass: boolean;
      expiresAt: string;
    };
    const minutes = (Date.parse(access.expiresAt) - before) / 60_000;

    expect(server.seen).toContain(`POST /api/v1/admin/tenants/T1/access Bearer ${adminToken}`);
    expect(access.justification).toBe('debug the failures');
    expect(access.breakGlass).toBe(false);
    expect(Math.round(minutes)).toBe(30);
    expect(server.seen).toContain(`POST /api/v1/admin/users/u-anda/impersonate Bearer ${adminToken}`);

    expect(JSON.parse(server.bodies.get('/api/v1/admin/users/u-anda/impersonate')!)).toEqual({
      accessGrantId: 'G1',
      justification: 'debug the failures',
    });

    expect(server.seen).toContain(`GET /api/v1/auth/me Bearer ${actToken}`);
    expect(router.currentRoute.value.name).toBe('dashboard-home');
    expect(localStorage.getItem('nnz.channel')).toBe('T1');
    expect(text()).toContain('anda_six');
    expect(exits()).toHaveLength(1);
    expect(html()).not.toContain('stoney');
  });

  it('keeps acting after a reload: the refresh hands back the target and the Exit stays', async () => {
    await load('#access_token=admin-token');
    await actFromRow();
    server.seen.length = 0;
    await load();

    expect(server.seen).toContain('POST /api/v1/auth/refresh none');
    expect(server.seen).toContain(`GET /api/v1/auth/me Bearer ${actToken}`);
    expect(text()).toContain('anda_six');
    expect(exits()).toHaveLength(1);
    expect(html()).not.toContain('stoney');
  });

  it('exits through the bot, takes the admin session back and returns to the page the admin left', async () => {
    await load('#access_token=admin-token');
    await actFromRow();
    await load();
    exits()[0]!.click();
    await settle();

    expect(server.seen).toContain(`POST /api/v1/auth/impersonation/exit Bearer ${actToken}`);
    expect(server.seen).toContain(`GET /api/v1/auth/me Bearer ${adminToken}`);
    expect(router.currentRoute.value.fullPath).toBe('/admin/channels');
    expect(localStorage.getItem('nnz.channel')).toBe('');
    expect(exits()).toHaveLength(0);
    expect(text()).toContain('Stoney_Eagle');
  });

  it('shows the reason the bot gives when it refuses the person', async () => {
    server.refusal = {
      status: 403,
      code: 'TARGET_OUTSIDE_SESSION',
      message: 'This person is not part of the channel you opened the session for.',
    };

    await load('#access_token=admin-token');
    await actFromRow();

    expect(dialog().querySelector('[role="alert"]')!.textContent).toContain(
      'This person is not part of the channel you opened the session for.',
    );

    expect(dialog().textContent).not.toContain('failed');
    expect(exits()).toHaveLength(0);
    expect(router.currentRoute.value.name).toBe('dashboard-admin-channels');
  });

  it('acts from the Access page through the same dialog, picking the channel by name', async () => {
    await load('#access_token=admin-token');
    await router.push('/admin/access');
    await settle();
    document.body.querySelector<HTMLElement>('[data-act-as]')!.click();
    await settle();

    expect(dialog().querySelector('[data-field-grant]')).toBeNull();

    await type('[data-act-as-channel-search]', 'anda');
    byText(dialog(), '[data-act-as-channel-option]', 'anda_six').click();
    await settle();
    await type('[data-act-as-reason]', 'ticket 42');
    byText(dialog(), 'button', 'Act as').click();
    await settle();

    expect(server.seen).toContain(`POST /api/v1/admin/tenants/T1/access Bearer ${adminToken}`);
    expect(server.seen).toContain(`POST /api/v1/admin/users/u-anda/impersonate Bearer ${adminToken}`);
    expect(exits()).toHaveLength(1);
  });
});
