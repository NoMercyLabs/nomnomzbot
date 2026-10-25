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

import en from '../i18n/en.json';
import { dashboardSettingsRoutes } from '../../settings/route';
import { dashboardSupportersRoute } from '../route';
import SupporterSourcesView from '../SupporterSourcesView.vue';
import SupportersView from '../SupportersView.vue';

type Sent = {
  method: string;
  path: string;
  body: unknown;
};

const sent: Sent[] = [];
const held = vi.hoisted(() => ({ keys: ['supporters:read', 'supporters:config:write'] }));

const reply = (data: unknown): unknown => ({
  data: { data },
  response: { status: 200, ok: true },
});

const stored = [
  {
    sourceKey: 'kofi',
    connectionMode: 'webhook',
    hasSecret: true,
    isEnabled: true,
    status: 'idle',
    endpointUrl: 'https://bot.example/hooks/kofi/abc',
  },
  {
    sourceKey: 'streamlabs',
    connectionMode: 'socket',
    hasSecret: true,
    isEnabled: false,
    status: 'idle',
  },
];

const integrations = [
  {
    provider: 'patreon',
    connected: true,
    accountName: 'Streamer',
    connectionId: '01JABCDEFGHJKMNPQRSTVWXYZ0',
  },
];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<unknown> => {
      if (path.includes('/supporters/connections'))
        return reply(stored);

      return path.includes('/connections')
        ? reply(integrations)
        : reply([]);
    },
    PUT: async (path: string, init?: { body?: unknown }): Promise<unknown> => {
      sent.push({
        method: 'PUT',
        path,
        body: init?.body,
      });

      return reply(null);
    },
    DELETE: async (path: string, init?: { params?: { path?: unknown } }): Promise<unknown> => {
      sent.push({
        method: 'DELETE',
        path,
        body: init?.params?.path,
      });

      return reply(null);
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/components/layout/UserName.vue', () => ({
  default: { template: '<span />' },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  sent.length = 0;
  held.keys = ['supporters:read', 'supporters:config:write'];
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

type Page = typeof SupporterSourcesView | typeof SupportersView;

async function open(component: Page = SupporterSourcesView): Promise<void> {
  mounted = mount(component, {
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

function row(key: string): HTMLElement {
  return document.body.querySelector<HTMLElement>(`[data-source="${key}"]`) as HTMLElement;
}

async function openSource(key: string): Promise<void> {
  document.body.querySelector<HTMLElement>(`[data-source-action="${key}"]`)?.click();
  await flushPromises();
}

async function type(selector: string, value: string): Promise<void> {
  const input = document.body.querySelector<HTMLInputElement>(selector) as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input'));
  await flushPromises();
}

function saveButton(): HTMLButtonElement {
  return document.body.querySelector<HTMLButtonElement>('[data-form-action]') as HTMLButtonElement;
}

async function save(): Promise<void> {
  document.body.querySelector<HTMLFormElement>('form')?.dispatchEvent(new Event('submit'));
  await flushPromises();
}

describe('supporter sources list', () => {
  it('lists every source once with its own state', async () => {
    await open();

    expect(document.body.querySelectorAll('[data-source]')).toHaveLength(11);
    expect(row('kofi').textContent).toContain(en.dashboard.supporters.sources.state.connected);
    expect(row('streamlabs').textContent).toContain(en.dashboard.supporters.sources.state.paused);
    expect(row('pally').textContent).toContain(en.dashboard.supporters.sources.state.none);
  });

  it('puts no form and no secret box on the page itself', async () => {
    await open();

    expect(document.body.querySelector('form')).toBeNull();
    expect(document.body.querySelector('input')).toBeNull();
  });

  it('offers Set up for a new source and Manage for a stored one, both outline', async () => {
    await open();

    const fresh = document.body.querySelector<HTMLElement>('[data-source-action="pally"]');
    const known = document.body.querySelector<HTMLElement>('[data-source-action="kofi"]');

    expect(fresh?.textContent?.trim()).toBe(en.dashboard.supporters.sources.setUp);
    expect(known?.textContent?.trim()).toBe(en.dashboard.supporters.sources.manage);
    expect(fresh?.getAttribute('data-variant')).toBe('outline');
    expect(known?.getAttribute('data-variant')).toBe('outline');
  });

  it('removes a stored source after the confirm, and offers Remove on no source that is not set up', async () => {
    await open();

    expect(document.body.querySelector('[data-source-remove="patreon"]')).toBeNull();

    document.body.querySelector<HTMLElement>('[data-source-remove="kofi"]')?.click();
    await settle();

    expect(sent.filter((call) => call.method === 'DELETE')).toHaveLength(0);

    document.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await settle();

    const removed = sent.filter((call) => call.method === 'DELETE');

    expect(removed).toHaveLength(1);
    expect(removed[0]?.path).toBe('/api/v{version}/supporters/connections/{sourceKey}');
    expect(removed[0]?.body).toMatchObject({ sourceKey: 'kofi' });
  });

  it('shows no setup button to a person who cannot write the connections', async () => {
    held.keys = ['supporters:read'];
    await open();

    expect(document.body.querySelectorAll('[data-source]')).toHaveLength(11);
    expect(document.body.querySelector('[data-source-action]')).toBeNull();
  });
});

describe('supporter source dialog', () => {
  it('shows the delivery endpoint of a webhook source from the server', async () => {
    await open();
    await openSource('kofi');

    expect(document.body.querySelector<HTMLInputElement>('[data-field="endpointUrl"]')?.value).toBe(
      'https://bot.example/hooks/kofi/abc',
    );
  });

  it('keeps Save off until the secret is typed, then sends it with the provider mode', async () => {
    await open();
    await openSource('pally');

    expect(saveButton().disabled).toBe(true);

    await type('[data-field="authSecret"]', 's3cret-token');

    expect(saveButton().disabled).toBe(false);

    await save();

    expect(sent).toHaveLength(1);

    expect(sent[0]?.body).toEqual({
      sourceKey: 'pally',
      connectionMode: 'ws',
      authSecret: 's3cret-token',
      integrationConnectionId: null,
      isEnabled: true,
    });

    expect(document.body.textContent).not.toContain('s3cret-token');
  });

  it('says a secret is stored for a saved source and sends the new state of the switch only', async () => {
    await open();
    await openSource('streamlabs');

    expect(
      document.body.querySelector<HTMLInputElement>('[data-field="authSecret"]')?.placeholder,
    ).toBe(en.dashboard.supporters.connections.secretStored);

    expect(saveButton().disabled).toBe(true);

    document.body.querySelector<HTMLElement>('[role="switch"]')?.click();
    await flushPromises();
    await save();

    expect(sent[0]?.body).toEqual({
      sourceKey: 'streamlabs',
      connectionMode: 'socket',
      authSecret: null,
      integrationConnectionId: null,
      isEnabled: true,
    });
  });
});

describe('where the page lives', () => {
  it('serves the settings route with the sources page, not the supporters page', () => {
    const settings = dashboardSettingsRoutes.find(
      (entry) => entry.name === 'dashboard-settings-supporter-sources',
    );

    expect(settings?.component).toBeDefined();
    expect(settings?.component).not.toBe(dashboardSupportersRoute.component);
  });

  it('leaves the connection form off the supporters page', async () => {
    await open(SupportersView);

    expect(document.body.querySelector('[data-view="supporter-connections"]')).toBeNull();
    expect(document.body.querySelector('form')).toBeNull();
  });
});
