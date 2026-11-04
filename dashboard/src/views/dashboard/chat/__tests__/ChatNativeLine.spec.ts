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

import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it } from 'vitest';

import type { ChatLine } from '@/api/chat';
import { i18n } from '@/i18n';
import ChatNativeLine from '../components/ChatNativeLine.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

const base = {
  id: 'line-1',
  channelId: 'c1',
  userId: 'u1',
  displayName: 'Viewer',
  username: 'viewer',
  message: 'Welcome',
  fragments: [],
  badges: [],
  color: null,
  timestamp: '2026-11-04T12:00:00Z',
  provider: 'twitch',
} as unknown as ChatLine;

const noticeTypes = [
  'sub', 'resub', 'sub_gift', 'community_sub_gift', 'gift_paid_upgrade',
  'prime_paid_upgrade', 'pay_it_forward', 'raid', 'unraid', 'announcement',
  'bits_badge_tier', 'charity_donation', 'watch_streak', 'modiversary',
  'gifted_drops_summary', 'shared_chat_sub', 'shared_chat_resub',
  'shared_chat_sub_gift', 'shared_chat_community_sub_gift',
  'shared_chat_gift_paid_upgrade', 'shared_chat_prime_paid_upgrade',
  'shared_chat_pay_it_forward', 'shared_chat_raid', 'shared_chat_unraid',
  'shared_chat_announcement', 'shared_chat_bits_badge_tier',
  'shared_chat_charity_donation', 'shared_chat_modiversary',
  'shared_chat_gifted_drops_summary', 'unknown',
] as const satisfies readonly string[];

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('nl', nl);
  i18n.global.locale.value = 'en';
});

describe('Twitch native feed lines', () => {
  it.each(noticeTypes)('renders notice type %s as a feed line', (noticeType) => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'notice',
        noticeType,
        systemText: 'A Twitch notice',
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-native-kind="notice"]').exists()).toBe(true);

    expect(wrapper.text()).toContain(noticeType.endsWith('announcement')
      ? 'Welcome'
      : 'A Twitch notice');
  });

  it('renders the announcement block, color token, title, and message', () => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'notice',
        noticeType: 'announcement',
        announcementColor: 'PURPLE',
        systemText: 'Viewer announced',
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-native-kind="notice"]').exists()).toBe(true);
    expect(wrapper.find('[data-announcement-color="PURPLE"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Announcement');
    expect(wrapper.text()).toContain('Viewer');
    expect(wrapper.text()).toContain('Welcome');
  });

  it.each([
    ['PRIMARY', 'border-primary'],
    ['BLUE', 'border-role-artist'],
    ['GREEN', 'border-success'],
    ['ORANGE', 'border-warning'],
    ['PURPLE', 'border-secondary'],
  ] as const)('maps announcement color %s to the %s token', (color, token) => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'notice',
        noticeType: 'announcement',
        announcementColor: color,
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find(`[data-announcement-color="${color}"] > div`).classes()).toContain(token);
  });

  it('renders a subscription notice and shared channel source', () => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'notice',
        noticeType: 'shared_chat_sub',
        systemText: 'Viewer subscribed',
        sourceChannelName: 'Other Stream',
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-source-channel]').text()).toContain('Other Stream');
    expect(wrapper.text()).toContain('Twitch subscription');
    expect(wrapper.text()).toContain('Viewer subscribed');
  });

  it('renders a moderation clear line', () => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'moderation',
        noticeType: 'chat_clear',
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-native-kind="moderation"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Chat was cleared');
  });

  it('renders a visibly held message for moderators', () => {
    const wrapper = mount(ChatNativeLine, {
      props: { line: {
        ...base,
        kind: 'held',
        heldStatus: 'held',
        message: 'private message',
      } },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-native-kind="held"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Held for review');
    expect(wrapper.text()).toContain('private message');
  });
});
