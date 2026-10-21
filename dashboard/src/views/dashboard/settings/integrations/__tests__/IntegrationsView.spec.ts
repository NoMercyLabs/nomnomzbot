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
const ownClientId = 'spotify-client-9f2a';
let spotifyConnected = true;
let spotifyStored = {
  clientId: ownClientId as string | null,
  hasClientSecret: true,
  hasSharedApp: false,
};

beforeEach(() => {
  spotifyConnected = true;

  for (const verb of [http.GET, http.POST, http.PUT, http.DELETE]) {
    verb.mockReset();
  }

  spotifyStored = {
    clientId: ownClientId,
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
          capabilities: {
            playback: true,
            library: false,
            premium: true,
          },
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
          connected: spotifyConnected,
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

    const summary = row('spotify').querySelector('[data-slot="summary"]')?.textContent ?? '';

    expect(summary).toContain('Spotify');
    expect(summary).toContain('stoney');
    expect(summary).toContain(en.dashboard.settings.integrations.needsReauth);
    expect(row('spotify').textContent).toContain('Plays music.');
    expect(row('spotify').querySelector('[data-slot="scope-sets"]')).toBeNull();
    expect(row('spotify').querySelector('[data-slot="capabilities"]')).toBeNull();

    await click(row('spotify').querySelector('[data-action="details"]'));

    const scopeText = row('spotify').querySelector('[data-slot="scope-sets"]')?.textContent;

    expect(scopeText).toContain(en.dashboard.settings.integrations.scopeSetNames.spotify_playback);
    expect(scopeText).not.toContain('spotify.playback');

    expect(row('spotify').querySelector('[data-slot="capabilities"]')?.textContent).toContain(
      en.dashboard.settings.integrations.capabilityNames.playback,
    );

    expect(row('spotify').querySelector('[data-slot="capabilities"]')?.textContent).not.toContain(
      en.dashboard.settings.integrations.capabilityNames.library,
    );

    expect(row('patreon').textContent).toContain(en.dashboard.settings.integrations.notConnected);
  });

  it('names the premium ability in words and ranks the row buttons: Disconnect is quiet, Edit is visible', async () => {
    await open();
    await click(row('spotify').querySelector('[data-action="details"]'));

    const text = row('spotify').querySelector('[data-slot="capabilities"]')?.textContent ?? '';

    expect(text).toContain(en.dashboard.settings.integrations.capabilityNames.premium);
    expect(text).not.toContain('premium');
    expect(action('spotify', 'disconnect')?.getAttribute('data-variant')).toBe('ghost');
    expect(action('spotify', 'edit-credentials')?.getAttribute('data-variant')).toBe('secondary');
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
  // The app in use lives on the Spotify row itself, not in a card of its own.
  const panel = (): HTMLElement => row('spotify');
  const field = (name: string): HTMLInputElement | null =>
    document.body.querySelector<HTMLInputElement>(`input[name="${name}"]`);
  const dialog = (): HTMLElement | null =>
    document.body.querySelector<HTMLElement>('[role="dialog"]');
  const words = en.dashboard.settings.integrations.spotify;

  async function type(input: HTMLInputElement, value: string): Promise<void> {
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  it('names the own app by the last 4 characters of its client id and shows no input while connected', async () => {
    await open();

    expect(panel().textContent).toContain('9f2a');
    expect(panel().textContent).not.toContain(ownClientId);
    expect(panel().textContent).not.toContain('s3cret');
    expect(field('clientId')).toBeNull();
    expect(field('clientSecret')).toBeNull();
    expect(dialog()).toBeNull();
  });

  it('puts the app line and the Edit button inside the Spotify row, and no other row carries them', async () => {
    await open();

    expect(panel().querySelector('[data-slot="stored-credential"]')?.textContent).toContain(words.ownApp);
    expect(panel().querySelector('[data-action="edit-credentials"]')).not.toBeNull();
    expect(document.body.querySelectorAll('[data-action="edit-credentials"]')).toHaveLength(1);
    expect(document.body.querySelector('[data-view="spotify-credentials"]')).toBeNull();
  });

  it('opens the form in a dialog from Edit, with the stored client id and never the secret', async () => {
    await open();
    await click(panel().querySelector('[data-action="edit-credentials"]'));

    expect(dialog()).not.toBeNull();
    expect(field('clientId')?.value).toBe(ownClientId);
    expect(field('clientSecret')?.value).toBe('');
    expect(field('clientSecret')?.placeholder).toBe(words.secretKept);
    expect((document.body.querySelector('[data-form-action]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('saves both values with a PUT, closes the dialog and empties the secret', async () => {
    await open();
    await click(panel().querySelector('[data-action="edit-credentials"]'));
    await type(field('clientId') as HTMLInputElement, 'new-client-0001');
    await type(field('clientSecret') as HTMLInputElement, 's3cret');
    await click(document.body.querySelector('[data-form-action]'));

    expect(http.PUT).toHaveBeenCalledWith(`${base}/spotify/credentials`, {
      params: { path: { channelId: 'c1', version: '1' } },
      body: { clientId: 'new-client-0001', clientSecret: 's3cret' },
    });

    expect(dialog()).toBeNull();
    expect(document.body.textContent).not.toContain('s3cret');
    expect(panel().textContent).toContain('0001');
  });

  it('opens the form by itself only when Spotify is not connected, no shared app exists and nothing is stored', async () => {
    spotifyConnected = false;

    spotifyStored = {
      clientId: null,
      hasClientSecret: false,
      hasSharedApp: false,
    };

    await open();

    expect(dialog()).not.toBeNull();
    expect(field('clientId')?.value).toBe('');
  });

  it('keeps the form closed when Spotify is not connected but the shared app exists', async () => {
    spotifyConnected = false;

    spotifyStored = {
      clientId: null,
      hasClientSecret: false,
      hasSharedApp: true,
    };

    await open();

    expect(dialog()).toBeNull();
    expect(panel().textContent).toContain(words.usingShared);

    await click(panel().querySelector('[data-action="edit-credentials"]'));

    expect(dialog()).not.toBeNull();

    expect(
      document.body.querySelector('[data-action="clear-spotify"]'),
    ).toBeNull();
  });

  it('removes the own app from inside the dialog with a DELETE and offers the shared app by name', async () => {
    spotifyStored = {
      clientId: ownClientId,
      hasClientSecret: true,
      hasSharedApp: true,
    };

    await open();
    await click(panel().querySelector('[data-action="edit-credentials"]'));

    const clear = document.body.querySelector('[data-action="clear-spotify"]');

    expect(clear?.textContent).toContain(words.clearShared);
    await click(clear);

    expect(http.DELETE).toHaveBeenCalledWith(`${base}/spotify/credentials`, {
      params: { path: { channelId: 'c1', version: '1' } },
    });
  });

  it('calls the remove button "Remove my app" when no shared app exists', async () => {
    await open();
    await click(panel().querySelector('[data-action="edit-credentials"]'));

    expect(
      document.body.querySelector('[data-action="clear-spotify"]')?.textContent,
    ).toContain(words.clear);
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
