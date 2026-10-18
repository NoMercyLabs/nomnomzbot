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
import { computed } from 'vue';

import { integrationsKey, spotifyCredentialsKey } from '@/api/integrations';
import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import { configDomainKeys } from '@/realtime/configDomains';

import { dashboardSettingsRoutes } from '../../route';
import { answerHeldKeys } from '../../__tests__/heldKeys';
import IntegrationsView from '../IntegrationsView.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

type Call = { path: string; init: { params?: { path?: Record<string, string> }; body?: unknown } };

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const base = '/api/v{version}/channels/{channelId}/integrations';
const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

let mounted: VueWrapper | null = null;
let held: string[] = [];
let spotifyStored = {
  clientId: 'abc',
  hasClientSecret: true,
  hasSharedApp: false,
};

beforeEach(() => {
  for (const verb of [http.GET, http.POST, http.PUT, http.DELETE]) {
    verb.mockReset();
  }

  spotifyStored = {
    clientId: 'abc',
    hasClientSecret: true,
    hasSharedApp: false,
  };

  http.GET.mockImplementation(async (path) => {
    if (path === `${base}/status`) {
      return ok([
        {
          provider: 'spotify',
          connected: true,
          accountName: 'stoney',
          grantedScopeSets: ['spotify.playback'],
          capabilities: { playback: true, library: false },
          needsReauth: true,
          loginOnly: false,
          decryptFailed: false,
        },
        {
          provider: 'twitch',
          connected: true,
          grantedScopeSets: [],
          capabilities: {},
          needsReauth: false,
        },
      ]);
    }

    if (path === `${base}/spotify/credentials`)
      return ok(spotifyStored);

    if (path === `${base}/{integrationId}/blast-radius`) {
      return ok({
        isMinimum: true,
        totalReferences: 3,
        categories: [
          {
            categoryKey: 'unknown.key',
            count: 3,
            sample: ['Song request', 'Now playing'],
          },
        ],
      });
    }

    return ok({
      integrations: [
        {
          id: 'twitch',
          name: 'Twitch',
          category: 'platform',
          description: 'Main account.',
          connected: true,
        },
        {
          id: 'spotify',
          name: 'Spotify',
          category: 'music',
          description: 'Plays music.',
          connected: true,
          connectedAs: 'stoney',
          lastSync: '2026-10-01T10:00:00Z',
        },
        {
          id: 'patreon',
          name: 'Patreon',
          category: 'supporters',
          description: 'Tiers.',
          connected: false,
        },
        {
          id: 'shopify',
          name: 'Shopify',
          category: 'shop',
          description: 'Orders.',
          connected: false,
        },
      ],
    });
  });

  held = ['integration:read', 'integration:write'];

  answerHeldKeys(http.GET, () => held);

  http.POST.mockResolvedValue(ok({ authorizeUrl: 'https://auth.example/go', state: 's' }));

  http.PUT.mockImplementation(async (_path, init) => {
    const body = (init as Call['init']).body as { clientId: string };

    spotifyStored = {
      ...spotifyStored,
      clientId: body.clientId,
      hasClientSecret: true,
    };

    return ok(spotifyStored);
  });

  http.DELETE.mockResolvedValue({ response: { ok: true, status: 204 } });

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

async function open(): Promise<QueryClient> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(IntegrationsView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await settle();

  return queries;
}

async function click(el: Element | null): Promise<void> {
  (el as HTMLElement).click();
  await settle();
}

const row = (id: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-integration="${id}"]`) as HTMLElement;
const action = (id: string, name: string): HTMLElement | null =>
  row(id).querySelector(`[data-action="${name}"]`);

describe('Integrations page', () => {
  it('lists each service with its state, account, permission sets and capabilities', async () => {
    await open();

    expect(row('spotify').textContent).toContain('Plays music.');
    expect(row('spotify').textContent).toContain('Connected as stoney');
    expect(row('spotify').textContent).toContain(en.dashboard.settings.integrations.needsReauth);

    expect(row('spotify').querySelector('[data-slot="scope-sets"]')?.textContent).toContain(
      'spotify.playback',
    );

    expect(row('spotify').querySelector('[data-slot="capabilities"]')?.textContent).toContain(
      'playback',
    );

    expect(row('spotify').querySelector('[data-slot="capabilities"]')?.textContent).not.toContain(
      'library',
    );

    expect(row('patreon').textContent).toContain(en.dashboard.settings.integrations.notConnected);
  });

  it('connects a service with its scope set and the page to come back to', async () => {
    const assign = vi.fn<(url: string) => void>();

    vi.stubGlobal('location', {
      ...window.location,
      href: 'http://app/settings',
      assign,
    });

    await open();
    await click(action('patreon', 'connect'));

    const [path, init] = http.POST.mock.calls[0] as [string, Call['init']];

    expect(path).toBe(`${base}/{provider}/connect`);
    expect(init.params?.path).toMatchObject({ channelId: 'c1', provider: 'patreon' });

    expect(init.body).toMatchObject({
      scopeSetKey: 'patreon.supporters',
      returnUrl: 'http://app/settings',
    });

    expect(assign).toHaveBeenCalledWith('https://auth.example/go');
    vi.unstubAllGlobals();
  });

  it('asks for the shop name before Shopify connects and sends it', async () => {
    vi.stubGlobal('location', { ...window.location, assign: vi.fn<(url: string) => void>() });
    await open();

    expect((action('shopify', 'connect') as HTMLButtonElement).disabled).toBe(true);

    const input = row('shopify').querySelector('input') as HTMLInputElement;

    input.value = 'my-shop';
    input.dispatchEvent(new Event('input'));
    await settle();
    await click(action('shopify', 'connect'));

    const call = http.POST.mock.calls[0] as [string, Call['init']];

    expect(call[1].body).toMatchObject({
      scopeSetKey: 'shopify.orders',
      shopDomain: 'my-shop',
    });

    vi.unstubAllGlobals();
  });

  it('never offers to disconnect the main Twitch account', async () => {
    await open();

    expect(action('twitch', 'disconnect')).toBeNull();
    expect(action('spotify', 'disconnect')).not.toBeNull();
  });

  it('shows what stops working before disconnect, and sends the delete only after the confirm', async () => {
    await open();
    await click(action('spotify', 'disconnect'));

    expect(http.GET).toHaveBeenCalledWith(`${base}/{integrationId}/blast-radius`, {
      params: {
        path: {
          channelId: 'c1',
          integrationId: 'spotify',
          version: '1',
        },
      },
    });

    expect(http.DELETE).not.toHaveBeenCalled();
    expect(document.body.textContent).toContain('at least 3');
    expect(document.body.textContent).toContain('Song request, Now playing');

    const confirm = [...document.body.querySelectorAll('[role="alertdialog"] button')].find(
      (button) => button.textContent?.trim() === en.dashboard.settings.integrations.disconnect,
    );

    await click(confirm ?? null);

    expect(http.DELETE).toHaveBeenCalledWith(`${base}/{integrationId}`, {
      params: {
        path: {
          channelId: 'c1',
          integrationId: 'spotify',
          version: '1',
        },
      },
    });
  });

  it('shows the refusal reason when the disconnect fails', async () => {
    await open();

    http.DELETE.mockResolvedValueOnce({
      error: { detail: 'Still in use.' },
      response: { ok: false, status: 400 },
    });

    await click(action('spotify', 'disconnect'));

    const confirm = [...document.body.querySelectorAll('[role="alertdialog"] button')].find(
      (button) => button.textContent?.trim() === en.dashboard.settings.integrations.disconnect,
    );

    await click(confirm ?? null);

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('Still in use.');
  });

  it('shows the error state when the list cannot load', async () => {
    http.GET.mockResolvedValue({ error: {}, response: { ok: false, status: 500 } });
    await open();

    expect(document.body.textContent).toContain(en.dashboard.settings.integrations.loadFailed);
  });
});

describe('Integrations page without the write key', () => {
  it('lists the services and their state but offers no connect, disconnect or own-app form', async () => {
    held = ['integration:read'];
    await open();

    expect(row('spotify').textContent).toContain('stoney');
    expect(action('patreon', 'connect')).toBeNull();
    expect(action('spotify', 'disconnect')).toBeNull();
    expect(document.body.querySelector('[data-action="save-spotify"]')).toBeNull();
    expect(document.body.querySelector('input[name="clientSecret"]')).toBeNull();
  });
});

describe('Spotify own app', () => {
  it('shows the stored client id and never the secret, and keeps Save off until both are typed', async () => {
    await open();

    const id = document.body.querySelector('input[name="clientId"]') as HTMLInputElement;
    const secret = document.body.querySelector('input[name="clientSecret"]') as HTMLInputElement;
    const save = document.body.querySelector('[data-action="save-spotify"]') as HTMLButtonElement;

    expect(id.value).toBe('abc');
    expect(secret.value).toBe('');
    expect(secret.placeholder).toBe(en.dashboard.settings.integrations.spotify.secretKept);
    expect(save.disabled).toBe(true);
  });

  it('saves both values with a PUT and empties the secret field', async () => {
    await open();

    const id = document.body.querySelector('input[name="clientId"]') as HTMLInputElement;
    const secret = document.body.querySelector('input[name="clientSecret"]') as HTMLInputElement;

    id.value = 'new-id';
    id.dispatchEvent(new Event('input'));
    secret.value = 's3cret';
    secret.dispatchEvent(new Event('input'));
    await settle();
    await click(document.body.querySelector('[data-action="save-spotify"]'));

    expect(http.PUT).toHaveBeenCalledWith(`${base}/spotify/credentials`, {
      params: { path: { channelId: 'c1', version: '1' } },
      body: { clientId: 'new-id', clientSecret: 's3cret' },
    });

    expect(
      (document.body.querySelector('input[name="clientSecret"]') as HTMLInputElement).value,
    ).toBe('');
  });

  it('offers no shared app when the server reports none', async () => {
    await open();

    const clear = document.body.querySelector('[data-action="clear-spotify"]');
    const panel = document.body.querySelector('[data-view="spotify-credentials"]');

    const words = en.dashboard.settings.integrations.spotify;

    expect(panel?.textContent).not.toContain(words.clearShared);
    expect(panel?.textContent).not.toContain(words.descriptionShared);
    expect(clear?.textContent).toContain(words.clear);
  });

  it('offers the shared app when the server reports one', async () => {
    spotifyStored = {
      clientId: 'abc',
      hasClientSecret: true,
      hasSharedApp: true,
    };

    await open();

    const clear = document.body.querySelector('[data-action="clear-spotify"]');
    const panel = document.body.querySelector('[data-view="spotify-credentials"]');

    const words = en.dashboard.settings.integrations.spotify;

    expect(clear?.textContent).toContain(words.clearShared);
    expect(panel?.textContent).toContain(words.descriptionShared);
  });

  it('clears the stored app with a DELETE', async () => {
    await open();
    await click(document.body.querySelector('[data-action="clear-spotify"]'));

    expect(http.DELETE).toHaveBeenCalledWith(`${base}/spotify/credentials`, {
      params: { path: { channelId: 'c1', version: '1' } },
    });
  });
});

describe('Integrations live sync', () => {
  it('refreshes the list, the statuses and the Spotify app when the bot publishes an integrations change', () => {
    const keys = (configDomainKeys.integrations ?? []).map((build) => build('c1'));

    expect(keys).toContainEqual(integrationsKey('c1'));
    expect(keys).toContainEqual(spotifyCredentialsKey('c1'));
  });
});

describe('Integrations route', () => {
  it('resolves the sidebar entry to the Integrations page and not a scaffold', () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-integrations');
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-integrations',
    );

    expect(entry).toBeDefined();
    expect(record?.path).toBe('settings/integrations');
    expect(record?.meta).toEqual({ navMode: 'settings', action: 'integration:read' });
    expect(record?.component).toBeTypeOf('function');
  });
});
