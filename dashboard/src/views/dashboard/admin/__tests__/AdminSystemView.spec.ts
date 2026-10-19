// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';
import { adminHandlers } from '@/realtime/admin';
import { dashboardRoutes } from '@/router/dashboard';

import AdminSystemView from '../AdminSystemView.vue';
import en from '../i18n/en.json';
import { dashboardAdminSystemRoute } from '../route';

type Options = {
  params?: { query?: Record<string, unknown>; path?: Record<string, unknown> };
  body?: Record<string, unknown>;
};
type Call = {
  method: string;
  path: string;
  options?: Options;
};

const calls: Call[] = [];

const system = {
  overall: 'degraded',
  services: [
    {
      name: 'api',
      status: 'healthy',
      latencyMs: null,
    },
    {
      name: 'database',
      status: 'healthy',
      latencyMs: 12,
    },
    {
      name: 'bot',
      status: 'degraded',
      latencyMs: null,
    },
  ],
  botVersion: '1.4.2.0',
  memoryUsageMb: 512,
  cpuPercent: 7.5,
};

const stats = {
  totalChannels: 10,
  activeChannels: 8,
  totalUsers: 99,
  systemStatus: 'degraded',
  botUptimeSeconds: 90_061,
  eventsProcessedToday: 1234,
};

const summary = {
  rewrappedCount: 5,
  alreadyCurrentCount: 2,
  failures: [{ cryptoKeyId: '01HZ0000000000000000000000', reason: 'wrong key' }],
};

let rotateStatus = 200;
let statsStatus = 200;

// The signed-in admin holds every platform key, so every action button shows.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: true }, can: () => true }),
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

      if (path.endsWith('/admin/system'))
        return { data: { data: system }, response: { status: 200, ok: true } };

      if (statsStatus !== 200)
        return { error: { detail: 'no' }, response: { status: statsStatus, ok: false } };

      return { data: { data: stats }, response: { status: 200, ok: true } };
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        options,
      });

      if (rotateStatus !== 200)
        return { error: { detail: 'no' }, response: { status: rotateStatus, ok: false } };

      return { data: { data: summary }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  rotateStatus = 200;
  statsStatus = 200;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
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
  mounted = mount(AdminSystemView, {
    attachTo: document.body,
    global: {
      plugins: [i18n, pinia, [VueQueryPlugin, { queryClient }]],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';

type Field = HTMLElement & { value: string; type: string };

const field = (selector: string): Field => document.body.querySelector<Field>(selector)!;
const posts = (): Call[] => calls.filter((call) => call.method === 'POST');
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  );

async function type(selector: string, value: string): Promise<void> {
  const node = field(selector);

  node.value = value;
  node.dispatchEvent(new Event('input'));
  await settle();
}

async function openRotation(): Promise<void> {
  field('[data-rotate]').click();
  await settle();
}

describe('AdminSystemView', () => {
  it('shows every service the API reported with its status and latency, never a fixed list', async () => {
    await open();

    const rows = [...document.body.querySelectorAll('[data-service-row]')];
    const names = rows.map((row) => row.querySelector('[data-service-name]')?.textContent?.trim());

    expect(names).toEqual(['api', 'database', 'bot']);
    expect(rows[1]!.textContent).toContain('12 ms');
    expect(rows[2]!.textContent).toContain('Degraded');
    expect(field('[data-overall]').textContent).toContain('Degraded');
  });

  it('shows the version, memory, processor load and uptime of the running bot', async () => {
    await open();

    expect(text()).toContain('1.4.2.0');
    expect(text()).toContain('512 MB');
    expect(text()).toContain('7.5%');
    expect(text()).toContain('1d 1h 1m');
  });

  it('shows the channel and user counters the API reported', async () => {
    await open();

    expect(text()).toContain('Active channels');
    expect(text()).toContain('of 10 registered');
    expect(text()).toContain('Known users');
    expect(text()).toContain('99');
  });

  it('keeps the health and the process facts when the person lacks the analytics key', async () => {
    statsStatus = 403;

    await open();

    expect(document.body.querySelectorAll('[data-service-row]')).toHaveLength(3);
    expect(text()).toContain('512 MB');
    expect(text()).not.toContain('Active channels');
    expect(text()).not.toContain('Events today');
    expect(document.body.querySelector('[data-error]')).toBeNull();
  });

  it('says the platform totals need the analytics permission when the stats are missing', async () => {
    statsStatus = 403;

    await open();

    expect(document.body.querySelector('[data-totals]')).toBeNull();
    expect(field('[data-totals-locked]').textContent).toContain('platform analytics permission');
  });

  it('shows the platform totals panel and no lock note when the stats are present', async () => {
    await open();

    expect(field('[data-totals]').textContent).toContain('Known users');
    expect(document.body.querySelector('[data-totals-locked]')).toBeNull();
  });

  it('follows a hub push that carries no stats: the totals give way to the lock note', async () => {
    await open();

    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveSystemStatus')!;

    handler.handle({ queries: queryClient, channelId: null }, { system, stats: null } as never);

    await settle();

    expect(document.body.querySelector('[data-totals]')).toBeNull();
    expect(document.body.querySelector('[data-totals-locked]')).not.toBeNull();
    expect(document.body.querySelectorAll('[data-service-row]')).toHaveLength(3);
    expect(text()).toContain('512 MB');
  });

  it('follows the hub: a pushed status replaces the page without a reload', async () => {
    await open();

    const handler = adminHandlers.find((entry) => entry.event === 'ReceiveSystemStatus')!;

    handler.handle({ queries: queryClient, channelId: null }, {
      system: {
        ...system,
        overall: 'healthy',
        services: [{ name: 'api', status: 'healthy' }],
      },
      stats,
    } as never);

    await settle();

    expect(document.body.querySelectorAll('[data-service-row]')).toHaveLength(1);
    expect(field('[data-overall]').textContent).toContain('Healthy');
  });

  it('keeps the key fields masked and asks nothing of the server until the dialog is confirmed', async () => {
    await open();
    await openRotation();

    expect(field('[data-previous-key]').type).toBe('password');
    expect(field('[data-current-key]').type).toBe('password');
    expect(posts()).toHaveLength(0);
  });

  it('names the blast radius in the confirm dialog before the key is rotated', async () => {
    await open();
    await openRotation();

    const dialog = document.body.querySelector('[role="dialog"]')!.textContent ?? '';

    expect(dialog).toContain('bot tokens');
    expect(dialog).toContain('re-authorize');
  });

  it('sends nothing while a key is missing', async () => {
    await open();
    await openRotation();
    await type('[data-previous-key]', 'old');
    byText('[role="dialog"] button', 'Rotate key')!.click();
    await settle();

    expect(posts()).toHaveLength(0);
  });

  it('keeps Rotate key disabled until both keys are typed', async () => {
    await open();
    await openRotation();

    const action = (): HTMLButtonElement =>
      document.body.querySelector<HTMLButtonElement>('[data-form-action]')!;

    expect(action().disabled).toBe(true);

    await type('[data-previous-key]', 'old');
    expect(action().disabled).toBe(true);

    await type('[data-current-key]', 'new');
    expect(action().disabled).toBe(false);
  });

  it('gives every panel the same 16 px body inset as its title', async () => {
    await open();

    const bodies = [...document.body.querySelectorAll('[data-slot="panel-card"]')].map(
      (panel) => panel.children[1]!.className,
    );

    expect(bodies.length).toBeGreaterThanOrEqual(3);
    expect(bodies.every((body) => body.includes('p-4') && !body.includes('p-2'))).toBe(true);
  });

  it('rotates with both keys, shows the counted outcome including failures and clears the keys', async () => {
    await open();
    await openRotation();
    await type('[data-previous-key]', 'old');
    await type('[data-current-key]', 'new');
    byText('[role="dialog"] button', 'Rotate key')!.click();
    await settle();

    const sent = posts();

    expect(sent).toHaveLength(1);
    expect(sent[0]!.path).toBe('/api/v{version}/admin/security/rotate-encryption-key');
    expect(sent[0]!.options?.body).toEqual({ previousKey: 'old', currentKey: 'new' });

    expect(field('[data-rotation-outcome]').textContent).toContain('5 re-wrapped');
    expect(field('[data-rotation-outcome]').textContent).toContain('2 already current');
    expect(document.body.querySelectorAll('[data-rotation-failure]')).toHaveLength(1);
    expect(text()).toContain('wrong key');
    expect(document.body.querySelector('[data-previous-key]')).toBeNull();

    await openRotation();

    expect(field('[data-previous-key]').value).toBe('');
    expect(field('[data-current-key]').value).toBe('');
  });

  it('keeps the dialog and the typed keys open when the rotation fails', async () => {
    rotateStatus = 400;

    await open();
    await openRotation();
    await type('[data-previous-key]', 'old');
    await type('[data-current-key]', 'new');
    byText('[role="dialog"] button', 'Rotate key')!.click();
    await settle();

    expect(document.body.querySelector('[data-form-error]')).not.toBeNull();
    expect(field('[data-previous-key]').value).toBe('old');
    expect(document.body.querySelector('[data-rotation-outcome]')).toBeNull();
  });

  it('registers an admin-only route', () => {
    const children = dashboardRoutes.children ?? [];
    const registered = children.find((route) => route.name === 'dashboard-admin-system');

    expect(registered?.path).toBe('admin/system');
    expect(registered?.meta?.comingSoon).toBeUndefined();
    expect(registered?.beforeEnter).toBe(dashboardAdminSystemRoute.beforeEnter);
  });
});
