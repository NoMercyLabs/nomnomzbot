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
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ConnectionsPanel from '../ConnectionsPanel.vue';
import en from '../i18n/en.json';

type Sent = {
  method: string;
  path: string;
  body: unknown;
};

const sent: Sent[] = [];

const reply = (data: unknown): unknown => ({
  data: { data },
  response: { status: 200, ok: true },
});

const stored = [{
  sourceKey: 'kofi',
  connectionMode: 'webhook',
  hasSecret: true,
  isEnabled: true,
  status: 'idle',
}];

const integrations = [
  {
    provider: 'patreon',
    connected: true,
    accountName: 'Streamer',
    connectionId: '01JABCDEFGHJKMNPQRSTVWXYZ0',
  },
  {
    provider: 'shopify',
    connected: false,
    accountName: null,
    connectionId: null,
  },
];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<unknown> =>
      path.includes('/supporters/connections')
        ? reply(stored)
        : reply(integrations),
    PUT: async (path: string, init?: { body?: unknown }): Promise<unknown> => {
      sent.push({
        method: 'PUT',
        path,
        body: init?.body,
      });

      return reply(null);
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  sent.length = 0;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  mounted = mount(ConnectionsPanel, {
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

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

function field<T extends HTMLElement>(name: string): T {
  return document.body.querySelector<T>(`[data-field="${name}"]`) as T;
}

async function pick(source: string): Promise<void> {
  const box = field<HTMLSelectElement>('sourceKey');

  box.value = source;
  box.dispatchEvent(new Event('change'));
  await flushPromises();
}

async function save(): Promise<void> {
  document.body.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
  await flushPromises();
}

describe('supporter connection form', () => {
  it('sends the provider mode with the secret, then empties the box and never shows the secret', async () => {
    await open();
    await pick('kofi');

    const secret = field<HTMLInputElement>('authSecret');

    secret.value = 's3cret-token';
    secret.dispatchEvent(new Event('input'));
    await flushPromises();
    await save();

    expect(sent).toHaveLength(1);

    expect(sent[0]?.body).toEqual({
      sourceKey: 'kofi',
      connectionMode: 'webhook',
      authSecret: 's3cret-token',
      integrationConnectionId: null,
      isEnabled: true,
    });

    expect(field<HTMLInputElement>('authSecret').value).toBe('');
    expect(document.body.textContent).not.toContain('s3cret-token');
  });

  it('takes the connected OAuth grant for an OAuth provider and sends no secret', async () => {
    await open();
    await pick('patreon');

    expect(field('authSecret')).toBeNull();

    const grant = field<HTMLSelectElement>('integrationConnectionId');

    expect(grant.textContent).toContain('Streamer');
    expect(grant.textContent).not.toContain('shopify');

    grant.value = '01JABCDEFGHJKMNPQRSTVWXYZ0';
    grant.dispatchEvent(new Event('change'));
    await flushPromises();
    await save();

    expect(sent[0]?.body).toEqual({
      sourceKey: 'patreon',
      connectionMode: 'webhook',
      authSecret: null,
      integrationConnectionId: '01JABCDEFGHJKMNPQRSTVWXYZ0',
      isEnabled: true,
    });
  });

  it('says a secret is already stored for a saved connection', async () => {
    await open();
    await pick('kofi');

    expect(field<HTMLInputElement>('authSecret').placeholder).toBe(en.dashboard.supporters.connections.secretStored);
  });
});
