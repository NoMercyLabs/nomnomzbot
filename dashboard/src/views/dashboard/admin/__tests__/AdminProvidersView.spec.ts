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

import { queryClient } from '@/api/queryClient';
import { i18n } from '@/i18n';

import AdminProvidersView from '../AdminProvidersView.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import { dashboardAdminProvidersRoute } from '../route';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type Call = {
  path: string;
  body?: Record<string, unknown>;
  params?: Record<string, unknown>;
};

const puts: Call[] = [];
const deletes: Call[] = [];
const reads: string[] = [];
let refused: boolean = false;

const providers = [
  {
    provider: 'twitch',
    clientId: 'abc123client',
    clientIdSource: 'stored',
    secretSource: 'stored',
    appDecisionRecorded: true,
    supported: true,
    apiKeySource: null,
  },
  {
    provider: 'youtube',
    clientId: null,
    clientIdSource: 'unset',
    secretSource: 'environment',
    appDecisionRecorded: false,
    supported: true,
    apiKeySource: 'unset',
  },
  {
    provider: 'kick',
    clientId: null,
    clientIdSource: 'unset',
    secretSource: 'unset',
    appDecisionRecorded: false,
    supported: false,
    apiKeySource: null,
  },
];

vi.mock('vue-sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      reads.push(path);

      if (refused)
        return { error: { message: 'no' }, response: { status: 403, ok: false } };

      return { data: { status: 'ok', data: providers }, response: { status: 200, ok: true } };
    },
    PUT: async (path: string, options: Call): Promise<Answer> => {
      puts.push({
        path,
        body: options.body,
        params: options.params,
      });

      return { data: { status: 'ok' }, response: { status: 200, ok: true } };
    },
    DELETE: async (path: string, options: Call): Promise<Answer> => {
      deletes.push({ path, params: options.params });

      return { data: { status: 'ok' }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  puts.length = 0;
  deletes.length = 0;
  reads.length = 0;
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
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(AdminProvidersView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const one = <T extends HTMLElement>(selector: string): T =>
  document.body.querySelector<T>(selector)!;

async function type(selector: string, value: string): Promise<void> {
  const input = one<HTMLInputElement>(selector);

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function click(selector: string): Promise<void> {
  one(selector).click();
  await settle();
}

describe('AdminProvidersView', () => {
  it('lists every provider with the source of its id and secret, and never a stored secret', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-provider]')).toHaveLength(3);
    expect(one('[data-provider="twitch"]').textContent).toContain('abc123client');
    expect(one('[data-provider="twitch"] [data-secret-source]').textContent).toContain('Stored');
    expect(one('[data-provider="youtube"] [data-secret-source]').textContent).toContain('Environment');
    expect(one('[data-provider="kick"]').textContent).toContain('does not support');

    const secret = one<HTMLInputElement>('[data-provider="twitch"] [data-field="clientSecret"]');

    expect(secret.type).toBe('password');
    expect(secret.value).toBe('');
    expect(one('[data-restricted]').textContent).toContain('restricted option');
  });

  it('sends only the typed fields and clears the secret box after saving', async () => {
    await open();

    expect(one<HTMLButtonElement>('[data-provider="twitch"] [data-save]').disabled).toBe(true);

    await type('[data-provider="twitch"] [data-field="clientSecret"]', 'rotated-secret');
    await click('[data-provider="twitch"] [data-save]');

    expect(puts).toHaveLength(1);
    expect(puts[0]?.params).toMatchObject({ path: { provider: 'twitch' } });
    expect(puts[0]?.body).toEqual({ clientSecret: 'rotated-secret' });
    expect(one<HTMLInputElement>('[data-provider="twitch"] [data-field="clientSecret"]').value).toBe('');
    expect(reads.length).toBeGreaterThan(1);
  });

  it('sends the client id and an api key for youtube only', async () => {
    await open();

    expect(document.body.querySelector('[data-provider="twitch"] [data-field="apiKey"]')).toBeNull();

    await type('[data-provider="youtube"] [data-field="clientId"]', 'yt-client');
    await type('[data-provider="youtube"] [data-field="apiKey"]', 'yt-key');
    await click('[data-provider="youtube"] [data-save]');

    expect(puts[0]?.body).toEqual({ clientId: 'yt-client', apiKey: 'yt-key' });
  });

  it('clears stored credentials only after a second confirm', async () => {
    await open();
    await click('[data-provider="twitch"] [data-clear]');

    expect(deletes).toHaveLength(0);

    await click('[data-provider="twitch"] [data-clear-confirm]');

    expect(deletes).toHaveLength(1);
    expect(deletes[0]?.params).toMatchObject({ path: { provider: 'twitch' } });
  });

  it('says so when the platform admin may not read the providers', async () => {
    refused = true;
    await open();

    expect(document.body.textContent).toContain('You may not see the providers.');
    expect(document.body.querySelectorAll('[data-provider]')).toHaveLength(0);
  });

  it('is a registered admin route with copy in both locales', () => {
    expect(dashboardAdminProvidersRoute.name).toBe('dashboard-admin-providers');
    expect(dashboardAdminProvidersRoute.path).toBe('admin/providers');

    expect(Object.keys(nl.dashboard.adminProviders).sort())
      .toEqual(Object.keys(en.dashboard.adminProviders).sort());
  });
});
