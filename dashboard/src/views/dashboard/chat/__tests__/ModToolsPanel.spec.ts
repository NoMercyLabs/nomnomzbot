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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

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
vi.mock('vue-sonner', () => ({ toast: { error: vi.fn<(message: string) => void>(), success: vi.fn<(message: string) => void>() } }));
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

let removal = { enabled: false, applies: false };

function answerFor(path: string): unknown {
  if (path.includes('raid-assessments'))
    return [raid];

  if (path.includes('own-name-removal'))
    return removal;

  if (path.includes('roles/effective'))
    return { heldActionKeys: [] };

  return initial;
}

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
  removal = { enabled: false, applies: false };

  http.GET.mockImplementation(async (path) => ({ data: { data: answerFor(path) }, response: ok }));

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

  it('shows the bot reason in a toast when a toggle is refused, and keeps the saved value', async () => {
    http.PUT.mockResolvedValue({
      error: { message: 'Slow mode needs a delay of at least 3 seconds.' },
      response: { status: 400, ok: false },
    });

    const { wrapper, queries } = setup();

    await flushPromises();
    await wrapper.find('[data-mode="slowMode"]').trigger('click');
    await flushPromises();

    expect(toast.error).toHaveBeenCalledWith('errors.invalid Slow mode needs a delay of at least 3 seconds.');
    expect(queries.getQueryData(['channels', 'channel', 'chat-settings'])).toEqual(initial);
    wrapper.unmount();
  });

  it('shows a field error for out-of-range input and sends nothing', async () => {
    const { wrapper } = setup();

    await flushPromises();

    const input = wrapper.find<HTMLInputElement>('[data-duration="slowMode"]');

    await input.setValue('500');
    await input.trigger('change');
    await flushPromises();

    expect(http.PUT).not.toHaveBeenCalled();
    expect(wrapper.find('[data-duration-error="slowMode"]').text()).toBe('Enter 3 to 120 seconds.');
    wrapper.unmount();
  });

  it('accepts 0 for followers-only', async () => {
    const { wrapper } = setup();

    await flushPromises();

    const input = wrapper.find<HTMLInputElement>('[data-duration="followersOnly"]');

    expect(input.attributes('min')).toBe('0');
    await input.setValue('0');
    await input.trigger('change');
    await flushPromises();

    expect(http.PUT).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/chat/settings',
      expect.objectContaining({ body: { ...initial, followersOnlyDuration: 0 } }),
    );

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

  it('hides the spam switch where the bot is installed or the reader holds no role', async () => {
    const { wrapper } = setup();

    await flushPromises();

    expect(wrapper.find('[data-own-name-removal]').exists()).toBe(false);

    wrapper.unmount();
  });

  it('saves the moderator\'s own yes, reads the inbox again and rolls back when the bot refuses', async () => {
    removal = { enabled: false, applies: true };
    http.PUT.mockResolvedValue({ data: { data: { enabled: true, applies: true } }, response: ok });

    const { wrapper, queries } = setup();
    const inbox = ['channels', 'channel', 'action-required'];

    queries.setQueryData(inbox, []);
    await flushPromises();

    expect(wrapper.text()).toContain('Remove spam in my name');
    await wrapper.find('[data-own-name-removal]').trigger('click');
    await flushPromises();

    expect(http.PUT).toHaveBeenCalledWith(
      '/api/v{version}/channels/{channelId}/moderation/spam-defense/own-name-removal',
      expect.objectContaining({ body: { enabled: true } }),
    );

    expect(queries.getQueryData(['channels', 'channel', 'own-name-spam-removal']))
      .toEqual({ enabled: true, applies: true });

    expect(queries.getQueryState(inbox)?.isInvalidated).toBe(true);

    http.PUT.mockResolvedValue({ error: { detail: 'You hold no moderator role on this channel.' }, response: { status: 403, ok: false } });
    await wrapper.find('[data-own-name-removal]').trigger('click');
    await flushPromises();

    expect(queries.getQueryData(['channels', 'channel', 'own-name-spam-removal']))
      .toEqual({ enabled: true, applies: true });

    expect(toast.error).toHaveBeenCalledWith('errors.forbidden You hold no moderator role on this channel.');

    wrapper.unmount();
  });
});
