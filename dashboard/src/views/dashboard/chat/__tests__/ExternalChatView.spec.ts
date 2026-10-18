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
import { createMemoryHistory, createRouter } from 'vue-router';

import { TooltipProvider } from '@/components/ui/tooltip';
import type * as HubModule from '@/realtime/dashboardHub';
import { i18n } from '@/i18n';
import appWords from '@/i18n/locales/en.json';
import { externalChatHandlers, type ExternalChatPayload } from '@/realtime/externalChat';

import ExternalChatView from '../ExternalChatView.vue';
import en from '../i18n/en.json';

const hub = vi.hoisted(() => ({
  startDashboardHub: vi.fn<typeof HubModule.startDashboardHub>(async () => undefined),
  joinTwitchChat: vi.fn<typeof HubModule.joinTwitchChat>(async (_id: string) => ({
    success: true,
    error: null,
  })),
  leaveTwitchChat: vi.fn<typeof HubModule.leaveTwitchChat>(async (_id: string) => undefined),
  sendTwitchChat: vi.fn<typeof HubModule.sendTwitchChat>(
    async (
      _id: string,
      _text: string,
      _replyTo: string | null,
    ): Promise<HubModule.ExternalChatSendAnswer> => ({
      success: true,
      error: null,
      errorCode: null,
      missingScope: null,
    }),
  ),
}));

vi.mock('@/realtime/dashboardHub', () => hub);

const broadcasterId = '4242';

const payload = (id: string, text: string): ExternalChatPayload => ({
  kind: 'Message',
  broadcasterTwitchId: broadcasterId,
  messageId: id,
  chatterTwitchId: '7',
  chatterLogin: 'someviewer',
  chatterDisplayName: 'SomeViewer',
  color: '#ff0000',
  text,
  fragments: [],
  badges: [],
  replyParentMessageId: null,
  timestamp: '2026-10-17T10:00:00Z',
});

function push(event: ExternalChatPayload): void {
  externalChatHandlers[0].handle({ queries: new QueryClient(), channelId: null }, event);
}

async function open() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/me/:channel/twitch', component: ExternalChatView }],
  });

  await router.push(`/me/otherstreamer/twitch?id=${broadcasterId}`);
  await router.isReady();

  i18n.global.mergeLocaleMessage('en', appWords);
  i18n.global.mergeLocaleMessage('en', en);

  const wrapper = mount(
    {
      components: { ExternalChatView, TooltipProvider },
      template: '<TooltipProvider><ExternalChatView /></TooltipProvider>',
    },
    {
      attachTo: document.body,
      global: {
        plugins: [
          i18n,
          router,
          [VueQueryPlugin, { queryClient: new QueryClient() }],
          createPinia(),
        ],
      },
    },
  );

  await flushPromises();

  return wrapper;
}

describe('ExternalChatView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();

    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe(): void {}
        unobserve(): void {}
        disconnect(): void {}
      },
    );

    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(40);
    vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(4000);
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(600);
    HTMLElement.prototype.scrollTo = function (): void {} as typeof HTMLElement.prototype.scrollTo;
    hub.joinTwitchChat.mockResolvedValue({ success: true, error: null });

    hub.sendTwitchChat.mockResolvedValue({
      success: true,
      error: null,
      errorCode: null,
      missingScope: null,
    });
  });

  it('joins the channel by its Twitch id on mount', async () => {
    await open();

    expect(hub.joinTwitchChat).toHaveBeenCalledExactlyOnceWith(broadcasterId);
  });

  it('leaves the channel on unmount', async () => {
    const wrapper = await open();

    wrapper.unmount();

    expect(hub.leaveTwitchChat).toHaveBeenCalledExactlyOnceWith(broadcasterId);
  });

  it('renders a line the hub pushes and drops it when Twitch deletes it', async () => {
    const wrapper = await open();

    push(payload('m1', 'hello from another channel'));
    await flushPromises();

    expect(wrapper.text()).toContain('hello from another channel');
    expect(wrapper.text()).toContain('SomeViewer');

    push({ ...payload('m1', ''), kind: 'MessageDeleted' });
    await flushPromises();

    expect(wrapper.text()).not.toContain('hello from another channel');
  });

  it('ignores a line of another channel', async () => {
    const wrapper = await open();

    push({ ...payload('m2', 'wrong room'), broadcasterTwitchId: '999' });
    await flushPromises();

    expect(wrapper.text()).not.toContain('wrong room');
  });

  it('sends through the hub with the channel id and clears the box', async () => {
    const wrapper = await open();
    const box = wrapper.find('input[data-external-chat-input]');

    await box.setValue('hi there');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(hub.sendTwitchChat).toHaveBeenCalledExactlyOnceWith(broadcasterId, 'hi there', null);
    expect((box.element as HTMLInputElement).value).toBe('');
  });

  it('shows the permission prompt when the send lacks user:write:chat', async () => {
    hub.sendTwitchChat.mockResolvedValue({
      success: false,
      error: 'Missing required scope \'user:write:chat\'.',
      errorCode: 'missing_scope',
      missingScope: 'user:write:chat',
    });

    const wrapper = await open();

    await wrapper.find('input[data-external-chat-input]').setValue('hi');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    const prompt = wrapper.find('[data-test="scope"]');

    expect(prompt.exists()).toBe(true);
    expect(prompt.text()).toContain('user:write:chat');
    expect(wrapper.find('[data-test="regrant"]').exists()).toBe(true);
  });

  it('shows no moderation controls', async () => {
    const wrapper = await open();

    push(payload('m3', 'viewer only'));
    await flushPromises();

    expect(wrapper.find('[data-mod-tools-toggle]').exists()).toBe(false);
    expect(wrapper.find('[data-chat-ban]').exists()).toBe(false);
  });
});
