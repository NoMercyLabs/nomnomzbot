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

// Shared chat: a line shows the badges the chatter wears where they typed, the source channel as a small
// avatar, and this channel's own lines carry a light tint while another channel's lines are on screen.
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h } from 'vue';

import type { ChatLine as ChatLineData } from '@/api/chat';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';

import { chatDisplayDefaults } from '../chatDisplay';
import ChatLine from '../components/ChatLine.vue';
import en from '../i18n/en.json';

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: { value: true },
    can: (): boolean => false,
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1'), current: computed(() => null) }),
}));

const badge = (setId: string): Record<string, unknown> => ({
  setId,
  id: '1',
  info: null,
  urls: { '1x': `https://example.test/${setId}.png` },
});

const chatter = {
  id: 'm1',
  channelId: 'c1',
  userId: 'u1',
  displayName: 'Viewer',
  username: 'viewer',
  message: 'hello',
  fragments: [{ type: 'text', text: 'hello' }],
  badges: [badge('moderator')],
  color: '#ff69b4',
  timestamp: '2026-10-12T18:01:00Z',
  provider: 'twitch',
} as unknown as ChatLineData;

let mounted: VueWrapper | null = null;

beforeEach(() => {
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function show(line: ChatLineData, shared = false): void {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const host = defineComponent({
    render: () => h(TooltipProvider, null, () =>
      h(ChatLine, {
        line,
        channelId: 'c1',
        display: chatDisplayDefaults,
        index: 0,
        me: 'kitte',
        channelLogin: 'kitte',
        shared,
      })),
  });

  mounted = mount(host, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });
}

const badgeSources = (): string[] =>
  [...document.body.querySelectorAll<HTMLImageElement>('[data-badge]')].map((item) => item.src);

describe('Shared chat line', () => {
  it('shows the source badges instead of the badges when the line carries them', () => {
    show({
      ...chatter,
      sourceChannelName: 'Other Stream',
      sourceBadges: [badge('vip')],
    } as ChatLineData);

    expect(badgeSources()).toEqual(['https://example.test/vip.png']);
  });

  it('shows the badges when the line carries no source badges', () => {
    show(chatter);

    expect(badgeSources()).toEqual(['https://example.test/moderator.png']);
  });

  it('draws the source channel as an avatar labeled with its name and no "From" pill', () => {
    show({
      ...chatter,
      sourceChannelName: 'Other Stream',
      sourceChannelAvatarUrl: null,
    } as ChatLineData);

    const source = document.body.querySelector('[data-source-channel]');

    expect(source?.getAttribute('aria-label')).toBe('Other Stream');
    expect(source?.classList.contains('rounded-full')).toBe(true);
    expect(source?.classList.contains('size-4')).toBe(true);
    expect(document.body.textContent).not.toContain('From Other Stream');
  });

  it('tints a line from this channel only when the list shows another channel too', () => {
    show(chatter, true);

    expect(document.body.querySelector('[data-line-id="m1"]')?.getAttribute('data-own-channel')).toBe('true');
  });

  it('leaves this channel\'s line untinted when no shared session is on screen', () => {
    show(chatter, false);

    expect(document.body.querySelector('[data-line-id="m1"]')?.hasAttribute('data-own-channel')).toBe(false);
  });

  it('does not tint a line that came from another channel', () => {
    show({ ...chatter, sourceChannelName: 'Other Stream' } as ChatLineData, true);

    expect(document.body.querySelector('[data-line-id="m1"]')?.hasAttribute('data-own-channel')).toBe(false);
  });
});
