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
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';

import { dashboardSettingsRoutes } from '../../route';
import { accessPath } from '../../__tests__/heldKeys';
import FederationView from '../FederationView.vue';
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
let optIns: Body[] = [];
let held: string[] = [];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const optIn = (id: string, optInType: string, extra: Body = {}): Body => ({
  id,
  broadcasterId: 'c1',
  peerId: null,
  optInType,
  direction: 'both',
  isEnabled: true,
  ...extra,
});

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path });

      return path === accessPath
        ? ok({ heldActionKeys: held })
        : ok(optIns);
    },
    PUT: async (path: string, options: { body?: Body; params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'PUT',
        path,
        body: options.body,
        params: options.params,
      });

      return ok(optIn('n1', 'shared_chat_bans', options.body));
    },
    DELETE: async (path: string, options: { params?: Body }): Promise<Answer> => {
      calls.push({
        method: 'DELETE',
        path,
        params: options.params,
      });

      return { response: { status: 204, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  calls.length = 0;
  held = ['federation:optin:read', 'federation:optin:write', 'federation:optin:delete'];

  optIns = [
    optIn('o1', 'shared_ban_list', { direction: 'accept' }),
    optIn('o2', 'shared_trust_list', { isEnabled: false, direction: 'share' }),
    optIn('o3', 'shared_savings', { peerId: 'p9', direction: 'share' }),
  ];

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

  mounted = mount(FederationView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;
const row = (type: string): HTMLElement => q(`[data-opt-in="${type}"]`);

function labeled(type: string, label: string): HTMLElement {
  const all = Array.from(row(type).querySelectorAll<HTMLElement>('[data-labeled-switch]'));

  return all.find((el) => el.textContent?.trim().startsWith(label)) as HTMLElement;
}

function toggle(type: string, label: string): HTMLButtonElement {
  return labeled(type, label).querySelector('button') as HTMLButtonElement;
}

function isOn(type: string, label: string): boolean {
  return toggle(type, label).getAttribute('data-state') === 'checked';
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await settle();
}

const callTo = (method: string): Call | undefined => calls.find((call) => call.method === method);

describe('Federation', () => {
  it('shows an accept and a share switch per kind; a kind with no row or a disabled row has both off', async () => {
    await open();

    expect([isOn('shared_ban_list', 'Accept'), isOn('shared_ban_list', 'Share')]).toEqual([true, false]);
    expect([isOn('shared_trust_list', 'Accept'), isOn('shared_trust_list', 'Share')]).toEqual([false, false]);
    expect([isOn('shared_chat_bans', 'Accept'), isOn('shared_chat_bans', 'Share')]).toEqual([false, false]);
    expect(row('shared_chat_bans').querySelector('[data-segment]')).toBeNull();
    expect(document.body.querySelectorAll('[data-opt-in]').length).toBe(4);
  });

  it('sends accept when Accept is switched on from off', async () => {
    await open();
    await click(toggle('shared_chat_bans', 'Accept'));

    expect(callTo('PUT')?.params).toEqual({ path: { version: '1', channelId: 'c1' } });

    expect(callTo('PUT')?.body).toEqual({
      peerId: null,
      optInType: 'shared_chat_bans',
      direction: 'accept',
      isEnabled: true,
    });
  });

  it('sends both when Share is switched on while Accept is on', async () => {
    await open();
    await click(toggle('shared_ban_list', 'Share'));

    expect(callTo('PUT')?.body).toEqual({
      peerId: null,
      optInType: 'shared_ban_list',
      direction: 'both',
      isEnabled: true,
    });
  });

  it('keeps the other way when one of two is switched off', async () => {
    optIns = [optIn('o1', 'shared_ban_list', { direction: 'both' })];
    await open();
    await click(toggle('shared_ban_list', 'Accept'));

    expect(callTo('PUT')?.body).toEqual({
      peerId: null,
      optInType: 'shared_ban_list',
      direction: 'share',
      isEnabled: true,
    });
  });

  it('saves the same way disabled when the last switch goes off', async () => {
    await open();
    await click(toggle('shared_ban_list', 'Accept'));

    expect(callTo('PUT')?.body).toEqual({
      peerId: null,
      optInType: 'shared_ban_list',
      direction: 'accept',
      isEnabled: false,
    });
  });

  it('lists a choice that names one peer apart and turns it off with a DELETE of that row', async () => {
    await open();

    expect(q('[data-peer-opt-in="o3"]').textContent).toContain('p9');
    expect(document.body.querySelectorAll('[data-peer-opt-in]').length).toBe(1);

    await click(q('[data-peer-opt-in-disable]'));

    expect(callTo('DELETE')?.params).toEqual({
      path: {
        version: '1',
        channelId: 'c1',
        optInId: 'o3',
      },
    });
  });

  it('shows a person with only the read key the choices without a way to change them', async () => {
    held = ['federation:optin:read'];
    await open();

    expect(row('shared_ban_list').querySelector('button[role="switch"]')).toBeNull();
    expect(labeled('shared_ban_list', 'Accept').querySelector('[data-slot="enabled-state"]')?.getAttribute('data-state')).toBe('on');
    expect(labeled('shared_ban_list', 'Share').querySelector('[data-slot="enabled-state"]')?.getAttribute('data-state')).toBe('off');
    expect(document.body.querySelector('[data-peer-opt-in="o3"]')).not.toBeNull();
    expect(document.body.querySelector('[data-peer-opt-in-disable]')).toBeNull();

    expect(callTo('PUT')).toBeUndefined();
  });

  it('offers the per-peer off button only with the delete key', async () => {
    held = ['federation:optin:read', 'federation:optin:write'];
    await open();

    expect(toggle('shared_chat_bans', 'Share').disabled).toBe(false);
    expect(document.body.querySelector('[data-peer-opt-in-disable]')).toBeNull();
  });

  it('shows the reason when the bot refuses a change', async () => {
    await open();

    const { client } = await import('@/api/client');

    vi.spyOn(client, 'PUT').mockResolvedValueOnce({
      error: { detail: 'Not allowed.' },
      response: { status: 403, ok: false },
    } as never);

    await click(toggle('shared_chat_bans', 'Share'));

    expect(q('[role="alert"]').textContent).toContain('Not allowed.');
  });
});

describe('Federation route', () => {
  it('opens the real page from the sidebar entry, not a coming-soon scaffold', async () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-federation');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-federation');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ ...record!, path: `/app/${record!.path}` }],
    });
    const loaded = await (record!.component as () => Promise<{ default: unknown }>)();

    expect(entry).toBeDefined();

    expect(router.resolve({ name: 'dashboard-settings-federation' }).path).toBe('/app/settings/federation');

    expect(record?.meta).toEqual({ navMode: 'settings', action: 'federation:optin:read' });
    expect(loaded.default).toBe(FederationView);
  });
});
