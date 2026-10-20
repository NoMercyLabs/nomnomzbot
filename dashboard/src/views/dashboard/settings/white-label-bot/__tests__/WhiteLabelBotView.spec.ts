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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';

import { dashboardSettingsRoutes } from '../../route';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import en from '../i18n/en.json';
import { answerHeldKeys } from '../../__tests__/heldKeys';
import WhiteLabelBotView from '../WhiteLabelBotView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

type Call = [string, { params?: unknown; body?: { deviceCode: string } }];

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const statusPath = '/api/v{version}/channels/{channelId}/bot/status';
const devicePath = '/api/v{version}/auth/twitch/channels/{channelId}/bot/device';
const pollPath = '/api/v{version}/auth/twitch/channels/{channelId}/bot/device/poll';
const connectPath = '/api/v{version}/channels/{channelId}/bot/connect';
const deletePath = '/api/v{version}/channels/{channelId}/bot';

const connectedBot = {
  connected: true,
  login: 'nomnomz_helper',
  displayName: 'NomNomz Helper',
  profileImageUrl: null,
};

const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

let mounted: VueWrapper | null = null;
let held: string[] = [];

async function open(status: unknown): Promise<void> {
  http.GET.mockImplementation(async (path: string) =>
    path === statusPath
      ? ok(status)
      : ok({ authorizeUrl: 'https://id.twitch.tv/authorize?x=1', state: 's' }),
  );

  answerHeldKeys(http.GET, () => held);

  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(WhiteLabelBotView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const button = (label: string): HTMLButtonElement | undefined =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button, a')].find(
    (item) => item.textContent?.trim() === label,
  ) as HTMLButtonElement | undefined;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  held = ['channelbot:read', 'channelbot:connect', 'channelbot:disconnect', 'integration:write'];
  http.GET.mockReset();
  http.POST.mockReset();
  http.DELETE.mockReset();
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('White-label bot page', () => {
  it('shows the connected bot account with its name and login', async () => {
    await open(connectedBot);

    expect(document.body.textContent).toContain('NomNomz Helper');
    expect(document.body.textContent).toContain('@nomnomz_helper');
    expect(document.body.textContent).toContain('Connected');
    expect(button('Disconnect')).toBeDefined();
  });

  it('shows the connected bot to a person with only the read key but offers no connect, replace or disconnect', async () => {
    held = ['channelbot:read'];
    await open(connectedBot);

    expect(document.body.textContent).toContain('@nomnomz_helper');
    expect(button('Disconnect')).toBeUndefined();
    expect(button('Open Twitch')).toBeUndefined();
    expect(button('Connect your own bot')).toBeUndefined();
  });

  it('offers disconnect only with the disconnect key', async () => {
    held = ['channelbot:read', 'channelbot:connect', 'integration:write'];
    await open(connectedBot);

    expect(button('Disconnect')).toBeUndefined();

    mounted?.unmount();
    document.body.innerHTML = '';
    held = ['channelbot:read', 'channelbot:disconnect'];
    await open(connectedBot);

    expect(button('Disconnect')).toBeDefined();
  });

  it('shows the shared bot and the connect action when no own bot is connected', async () => {
    await open({ connected: false });

    expect(document.body.textContent).toContain('Your channel uses the shared NomNomzBot account.');
    expect(button('Connect your own bot')).toBeDefined();
    expect(button('Disconnect')).toBeUndefined();
  });

  it('reads a channel on the shared system bot as the shared bot: named, with Connect your own bot and no Disconnect', async () => {
    await open({
      connected: true,
      login: 'nomz_bot',
      displayName: 'nomz_bot',
      usesSharedBot: true,
    });

    expect(document.body.textContent).toContain('Your channel uses the shared bot nomz_bot');
    expect(document.body.textContent).not.toContain('Connected');
    expect(button('Connect your own bot')).toBeDefined();
    expect(button('Disconnect')).toBeUndefined();
    expect(button('Change account')).toBeUndefined();
  });

  it('disconnect sends DELETE for this channel after the confirm and goes back to the shared bot', async () => {
    http.DELETE.mockResolvedValue({ response: { ok: true, status: 200 } });
    await open(connectedBot);

    button('Disconnect')!.click();
    await flushPromises();

    expect(http.DELETE).not.toHaveBeenCalled();

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await flushPromises();

    const [path, init] = http.DELETE.mock.calls[0] as Call;

    expect(path).toBe(deletePath);
    expect(init.params).toEqual({ path: { version: '1', channelId: 'c1' } });
    expect(document.body.textContent).toContain('Your channel uses the shared NomNomzBot account.');
  });

  it('offers one action while idle: no browser sign-in row next to Connect your own bot', async () => {
    await open({ connected: false });

    expect(button('Connect your own bot')).toBeDefined();
    expect(button('Open Twitch')).toBeUndefined();
    expect(button('Sign in with the browser')).toBeUndefined();
    expect(document.body.textContent).not.toContain('if you can not use a code');
  });

  it('browser sign-in is the fallback inside the connect flow: asks the bot for the authorize URL and opens it', async () => {
    const opener = vi.fn<(url: string, target: string, features: string) => void>();

    vi.stubGlobal('open', opener);

    http.POST.mockResolvedValue(
      ok({
        deviceCode: 'dev-1',
        userCode: 'ABCD-EFGH',
        verificationUri: 'https://www.twitch.tv/activate',
        interval: 60,
        expiresIn: 600,
      }),
    );

    await open({ connected: false });

    button('Connect your own bot')!.click();
    await flushPromises();
    button('Sign in with the browser')!.click();
    await flushPromises();

    expect(http.GET.mock.calls.some((call) => call[0] === connectPath)).toBe(true);
    expect(opener).toHaveBeenCalledWith('https://id.twitch.tv/authorize?x=1', '_blank', 'noopener');
  });

  it('shows the bot picture when the server sends one, and a bot icon instead of initials when it sends none', async () => {
    await open({
      connected: true,
      login: 'nomz_bot',
      displayName: 'nomz_bot',
      usesSharedBot: true,
    });

    const identity = document.body.querySelector('[data-slot="bot-identity"]')!;

    expect(identity.querySelector('[data-slot="bot-fallback-icon"]')).not.toBeNull();
    expect(identity.textContent).not.toContain('NO');
  });

  it('device login shows the code, polls with the device code and shows the bot once authorized', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    http.POST.mockImplementation(async (path: string) =>
      path === devicePath
        ? ok({
            deviceCode: 'dev-1',
            userCode: 'ABCD-EFGH',
            verificationUri: 'https://www.twitch.tv/activate',
            interval: 1,
            expiresIn: 600,
          })
        : ok({ status: 'authorized', bot: connectedBot }),
    );

    await open({ connected: false });

    button('Connect your own bot')!.click();
    await flushPromises();

    expect(document.body.textContent).toContain('ABCD-EFGH');

    await vi.advanceTimersByTimeAsync(1100);
    await flushPromises();

    const polls = http.POST.mock.calls.filter((call) => call[0] === pollPath) as Call[];

    expect(polls).toHaveLength(1);
    expect(polls[0]![1].body).toEqual({ deviceCode: 'dev-1' });
    expect(document.body.textContent).toContain('NomNomz Helper');
    expect(document.body.textContent).not.toContain('ABCD-EFGH');
  });

  it('device login that expires says so and offers a new code', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });

    http.POST.mockImplementation(async (path: string) =>
      path === devicePath
        ? ok({
            deviceCode: 'dev-2',
            userCode: 'WXYZ-1234',
            verificationUri: 'https://www.twitch.tv/activate',
            interval: 1,
            expiresIn: 600,
          })
        : ok({ status: 'expired' }),
    );

    await open({ connected: false });

    button('Connect your own bot')!.click();
    await flushPromises();
    await vi.advanceTimersByTimeAsync(1100);
    await flushPromises();

    expect(document.body.textContent).toContain('The code ran out of time.');
    expect(button('Try again')).toBeDefined();
  });
});

describe('White-label bot sidebar entry', () => {
  it('the sidebar route resolves to its own page and is not a coming-soon scaffold', () => {
    const name = 'dashboard-settings-white-label-bot';
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === name);
    const record = dashboardSettingsRoutes.find((route) => route.name === name);

    expect(entry).toBeDefined();
    expect(record).toBeDefined();
    expect(record!.path).toBe('settings/white-label-bot');
    expect(record!.meta).toMatchObject({ navMode: 'settings' });
    expect(record!.meta?.comingSoon).toBeUndefined();
  });
});
