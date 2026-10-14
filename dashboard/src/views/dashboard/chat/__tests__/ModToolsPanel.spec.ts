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
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createI18n } from 'vue-i18n';

import type { ChatSettings } from '@/api/modTools';

import ModToolsPanel from '../components/ModToolsPanel.vue';
import en from '../i18n/en.json';

type Reply = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Request = { body?: unknown };

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string) => Promise<Reply>>(),
  PUT: vi.fn<(path: string, request: Request) => Promise<Reply>>(),
  POST: vi.fn<(path: string, request: Request) => Promise<Reply>>(),
}));

vi.mock('@/api/client', () => ({ client: http }));
vi.mock('@/api/access', () => ({ useChannelAccess: () => ({ can: () => true }) }));

const initial = {
  slowMode: false,
  slowModeDelay: 30,
  subscriberOnly: false,
  emotesOnly: false,
  followersOnly: false,
  followersOnlyDuration: 10,
  uniqueChatMode: false,
  nonModeratorChatDelay: false,
  nonModeratorChatDelayDuration: 2,
} as const satisfies ChatSettings;
const ok = { status: 200, ok: true } as const;
const raid = {
  id: 'raid',
  raiderTwitchUserId: '123',
  raiderLogin: 'raider',
  raiderDisplayName: 'Raider',
  viewerCount: 1,
  earlierRaidCount: 0,
  isBanned: false,
  isNetworkBlocked: true,
  verdict: 'hostile',
  occurredAt: '2026-10-14T00:00:00Z',
};

function setup() {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const pinia = createPinia();

  setActivePinia(pinia);
  const wrapper = mount(ModToolsPanel, {
    props: { channelId: 'channel', channelName: 'streamer' },
    global: {
      plugins: [pinia, [VueQueryPlugin, { queryClient: queries }], createI18n({
        legacy: false,
        locale: 'en',
        messages: { en },
      })],
    },
  });

  return { wrapper, queries };
}

beforeEach(() => {
  http.GET.mockReset();
  http.PUT.mockReset();
  http.POST.mockReset();

  http.GET.mockImplementation(async (path) =>
    path.includes('raid-assessments')
      ? { data: { data: [raid] }, response: ok }
      : path.includes('roles/effective')
        ? { data: { data: { heldActionKeys: [] } }, response: ok }
        : { data: { data: initial }, response: ok });

  http.PUT.mockResolvedValue({ data: { data: { ...initial, slowMode: true } }, response: ok });
  http.POST.mockResolvedValue({ response: ok });
});

describe('Mod tools', () => {
  it('sends the complete PUT body for a mode and restores the previous state on failure', async () => {
    let reject: (reason: Error) => void = () => {};

    http.PUT.mockImplementation(() => new Promise<Reply>((_resolve, fail) => {
      reject = fail;
    }));

    const { wrapper, queries } = setup();

    await flushPromises();
    await wrapper.find('[data-mode="slowMode"]').trigger('click');
    await flushPromises();

    expect(http.PUT).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/chat/settings',
      expect.objectContaining({ body: { ...initial, slowMode: true } }),
    );

    expect(queries.getQueryData(['channels', 'channel', 'chat-settings'])).toEqual({ ...initial, slowMode: true });

    reject(new Error('network'));
    await flushPromises();

    expect(queries.getQueryData(['channels', 'channel', 'chat-settings'])).toEqual(initial);
    wrapper.unmount();
  });

  it('only clears chat or mass bans assessed hostile accounts after confirmation', async () => {
    const { wrapper } = setup();

    await flushPromises();
    await wrapper.findAll('button').find((button) => button.text().includes('Clear chat'))?.trigger('click');
    expect(http.POST).not.toHaveBeenCalled();
    await document.querySelector<HTMLButtonElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(http.POST).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/chat/clear',
      expect.anything(),
    );

    http.POST.mockClear();
    await wrapper.findAll('button').find((button) => button.text().includes('Mass ban'))?.trigger('click');
    expect(http.POST).not.toHaveBeenCalled();
    await document.querySelector<HTMLButtonElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(http.POST).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/actions/mass-ban',
      expect.objectContaining({ body: { targets: [{ twitchUserId: '123' }] } }),
    );

    wrapper.unmount();
  });
});
