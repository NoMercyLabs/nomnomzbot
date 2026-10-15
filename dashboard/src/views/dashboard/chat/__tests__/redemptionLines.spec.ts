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
import { mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import type { ChatLine as Line } from '@/api/chat';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';
import appWords from '@/i18n/locales/en.json';
import { activityLine, rewardLine } from '@/realtime/redemptionLines';

import { chatDisplayDefaults, gigantifiedEmoteHeight, type ChatDisplay } from '../chatDisplay';
import ChatLine from '../components/ChatLine.vue';
import RedemptionLine from '../components/RedemptionLine.vue';
import en from '../i18n/en.json';

i18n.global.mergeLocaleMessage('en', appWords);
i18n.global.mergeLocaleMessage('en', en);

const display = { ...chatDisplayDefaults } as const satisfies ChatDisplay;

function plugins() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { render: () => null } }],
  });

  const query = [VueQueryPlugin, { queryClient: new QueryClient() }] as const;

  return [i18n, createPinia(), router, query] as const;
}

function inTooltips(component: object, props: Record<string, unknown>) {
  const host = defineComponent(() =>
    () =>
      h(TooltipProvider, null, () =>
        h(component, props)));

  return mount(host, { global: { plugins: [...plugins()] } });
}

const powerUp = {
  broadcasterId: 'c1',
  redemptionId: 'r1',
  userId: 'u1',
  userLogin: 'party',
  userDisplayName: 'Party',
  userInput: null,
  timestamp: '2026-10-15T20:00:00Z',
} as const;

describe('a redemption in the chat feed', () => {
  it('names a custom reward, its cost in points and the words the viewer added', () => {
    const line = rewardLine({
      broadcasterId: 'c1',
      redemptionId: 'r0',
      rewardId: 'rw',
      rewardTitle: 'Hydrate',
      userId: 'u1',
      userDisplayName: 'Viewer',
      userInput: 'drink water',
      cost: 500,
      timestamp: '2026-10-15T20:00:00Z',
    } as Parameters<typeof rewardLine>[0]);
    const wrapper = inTooltips(RedemptionLine, { line, display });

    expect(wrapper.find('[data-redemption]').attributes('data-kind')).toBe('reward');
    expect(wrapper.find('[data-redemption-title]').text()).toBe('Hydrate');
    expect(wrapper.find('[data-redemption-cost]').text()).toBe('500 points');
    expect(wrapper.find('[data-redemption-input]').text()).toBe('drink water');
  });

  it('gives an On-Screen Celebration Power-up its Twitch name and its cost in Bits', () => {
    const line = activityLine({
      ...powerUp,
      kind: 'power_up',
      title: 'celebration',
      cost: 250,
      costUnit: 'bits',
    });
    const wrapper = inTooltips(RedemptionLine, { line, display });

    expect(wrapper.find('[data-redemption]').attributes('data-kind')).toBe('power_up');
    expect(wrapper.find('[data-redemption-title]').text()).toBe('On-Screen Celebration');
    expect(wrapper.find('[data-redemption-cost]').text()).toBe('250 Bits');
    expect(wrapper.find('[data-redemption-input]').exists()).toBe(false);
  });

  it('names an automatic reward by its Twitch name', () => {
    const line = activityLine({
      ...powerUp,
      kind: 'automatic_reward',
      title: 'send_highlighted_message',
      cost: 50,
      costUnit: 'channel_points',
    });
    const wrapper = inTooltips(RedemptionLine, { line, display });

    expect(wrapper.find('[data-redemption-title]').text()).toBe('Highlight My Message');
  });

  it('keeps a custom Power-up title as the streamer wrote it', () => {
    const line = activityLine({
      ...powerUp,
      kind: 'custom_power_up',
      title: 'celebration',
      cost: 100,
      costUnit: 'bits',
    });
    const wrapper = inTooltips(RedemptionLine, { line, display });

    expect(wrapper.find('[data-redemption-title]').text()).toBe('celebration');
  });
});

describe('a chat message sent with a Power-up', () => {
  const message = (messageType: string): Line =>
    ({
      id: 'm1',
      channelId: 'c1',
      userId: 'u1',
      displayName: 'Party',
      username: 'party',
      message: 'Kappa',
      fragments: [
        {
          type: 'emote',
          text: 'Kappa',
          emote: {
            id: '25',
            format: 'static',
            provider: 'Twitch',
            urls: { 1: 'https://cdn.test/kappa-1.png' },
            animated: false,
            zeroWidth: false,
          },
          cheermote: null,
          mention: null,
          linkPreview: null,
        },
      ],
      userType: 'viewer',
      isSubscriber: false,
      isVip: false,
      isModerator: false,
      isBroadcaster: false,
      isCheer: false,
      isCommand: false,
      badges: [],
      bitsAmount: 0,
      color: null,
      messageType,
      timestamp: '2026-10-15T20:00:00Z',
      provider: 'twitch',
    }) as unknown as Line;

  const mountLine = (line: Line) =>
    inTooltips(ChatLine, {
      line,
      channelId: 'c1',
      display,
      index: 0,
      me: null,
    });

  it('shows a Gigantified Emote large and says so above the text', () => {
    const wrapper = mountLine(message('power_ups_gigantified_emote'));
    const row = wrapper.find('[data-line-id="m1"]');

    expect(wrapper.find('[data-power-up]').text()).toBe('Gigantified Emote');
    expect(row.attributes('style')).toContain(`--emote-h: ${gigantifiedEmoteHeight}`);
  });

  it('says a Message Effect was used', () => {
    const wrapper = mountLine(message('power_ups_message_effect'));

    expect(wrapper.find('[data-power-up]').text()).toBe('Message Effect');
  });

  it('shows a plain message with no Power-up label at the normal emote size', () => {
    const wrapper = mountLine(message('text'));
    const row = wrapper.find('[data-line-id="m1"]');

    expect(wrapper.find('[data-power-up]').exists()).toBe(false);
    expect(row.attributes('style')).not.toContain(`--emote-h: ${gigantifiedEmoteHeight}`);
  });
});
