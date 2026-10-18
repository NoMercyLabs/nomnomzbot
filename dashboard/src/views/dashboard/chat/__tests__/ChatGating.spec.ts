// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Stoney 2026-10-18: "every tab and pannel and button and page needs to have proper permission checks
// before being shown". Each chat element shows only for the action key its endpoint checks.
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, defineComponent, h } from 'vue';

import type { ChatLine as ChatLineData } from '@/api/chat';
import { TooltipProvider } from '@/components/ui/tooltip';
import { i18n } from '@/i18n';

import { chatDisplayDefaults } from '../chatDisplay';
import ChatComposer from '../components/ChatComposer.vue';
import ChatIdentityPanel from '../components/ChatIdentityPanel.vue';
import ChatLine from '../components/ChatLine.vue';
import ViewerPanel from '../components/ViewerPanel.vue';
import en from '../i18n/en.json';

type Answer = {
  data?: { data: unknown };
  response: { status: number; ok: boolean };
};

const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: { value: true },
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1'), current: computed(() => null) }),
}));

const identity = {
  displayName: 'Kitte',
  color: '#0000FF',
  canManageColor: false,
  colorScope: 'user:manage:chat_color',
  namedColors: [],
  badges: [],
};

const member = {
  id: 'u1',
  internalUserId: 'g1',
  username: 'dukasoft',
  displayName: 'DukaSoft',
  profileImageUrl: null,
  messageCount: 1,
  watchHours: 1,
  commandsUsed: 0,
  trustLevel: 'viewer',
  isBanned: false,
  firstSeen: '2026-01-01T00:00:00Z',
  lastSeen: '2026-10-12T18:00:00Z',
  recentActivity: [],
  banHistory: [],
  isVip: null,
  isModerator: null,
};

const profile = {
  identity: {
    userId: 'g1',
    username: 'dukasoft',
    displayName: 'DukaSoft',
  },
  economy: { wallet: null },
};

function bodyFor(path: string): unknown {
  const bySuffix: [string, unknown][] = [
    ['/chat-identity', identity],
    ['/auth/me', { username: 'kitte' }],
    ['/profile', profile],
  ];

  return bySuffix.find(([suffix]) => path.endsWith(suffix))?.[1] ?? member;
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => ({
      data: { data: bodyFor(path) },
      response: { status: 200, ok: true },
    }),
    POST: async (): Promise<Answer> => ({
      data: { data: null },
      response: { status: 200, ok: true },
    }),
  },
}));

const chatter = {
  id: 'm1',
  channelId: 'c1',
  userId: 'u1',
  displayName: 'DukaSoft',
  username: 'dukasoft',
  message: 'hello',
  fragments: [{ type: 'text', text: 'hello' }],
  badges: [],
  color: '#ff69b4',
  timestamp: '2026-10-12T18:01:00Z',
  provider: 'twitch',
} as unknown as ChatLineData;

let mounted: VueWrapper | null = null;

beforeEach(() => {
  access.held = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function show(component: object, props: Record<string, unknown>): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const host = defineComponent({
    render: () => h(TooltipProvider, null, () =>
      h(component, props)),
  });

  mounted = mount(host, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return mounted;
}

const lineProps = {
  line: chatter,
  channelId: 'c1',
  display: chatDisplayDefaults,
  index: 0,
  me: 'kitte',
  channelLogin: 'kitte',
};

const actionsOf = (): string[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-line-action]')].map(
    (item) => item.dataset.lineAction ?? '',
  );

const buttonNames = (): string[] =>
  [...document.body.querySelectorAll('button')].map((item) => item.textContent?.trim() ?? '');

describe('Chat line actions', () => {
  it('shows no tray at all when the person holds no action key', async () => {
    await show(ChatLine, lineProps);

    expect(document.body.querySelector('[data-tray]')).toBeNull();
  });

  it.each([
    ['chat:send', 'reply'],
    ['moderation:delete_message', 'delete'],
    ['community:read', 'profile'],
  ])('shows only the action that %s unlocks', async (key, action) => {
    access.held = [key];

    await show(ChatLine, lineProps);

    expect(actionsOf()).toEqual([action]);
  });

  it('shows Timeout and Ban together for moderation:ban', async () => {
    access.held = ['moderation:ban'];

    await show(ChatLine, lineProps);

    expect(actionsOf()).toEqual(['timeout', 'ban']);
  });

  it('offers a participant Reply for chat:viewer:send, and nothing else', async () => {
    access.held = ['chat:viewer:send', 'moderation:ban', 'moderation:delete_message', 'community:read'];

    await show(ChatLine, { ...lineProps, viewer: true });

    expect(actionsOf()).toEqual(['reply']);
  });
});

describe('Chat composer', () => {
  it('shows no send box without chat:send', async () => {
    access.held = ['chat:read'];

    await show(ChatComposer, { channelId: 'c1', chatters: [] });

    expect(document.body.querySelector('[data-composer-box]')).toBeNull();
  });

  it('shows the send box for chat:send', async () => {
    access.held = ['chat:send'];

    await show(ChatComposer, { channelId: 'c1', chatters: [] });

    expect(document.body.querySelector('[data-composer-box]')).not.toBeNull();
  });

  it('asks a participant for chat:viewer:send, not chat:send', async () => {
    access.held = ['chat:send'];

    await show(ChatComposer, {
      channelId: 'c1',
      chatters: [],
      viewer: true,
    });

    expect(document.body.querySelector('[data-composer-box]')).toBeNull();
  });
});

describe('Viewer panel', () => {
  const panel = {
    userId: 'u1',
    channelId: 'c1',
    channelLogin: 'kitte',
    lines: [chatter],
    hour12: false,
  };

  it('hides the person and Profile without community:read', async () => {
    access.held = ['moderation:ban'];

    await show(ViewerPanel, panel);

    expect(document.body.querySelector('[data-viewer-profile]')).toBeNull();
    expect(buttonNames()).not.toContain(en.dashboard.chat.viewer.timeout);
  });

  it('hides Timeout and Ban without moderation:ban', async () => {
    access.held = ['community:read'];

    await show(ViewerPanel, panel);

    expect(document.body.querySelector('[data-viewer-profile]')).not.toBeNull();
    expect(buttonNames()).not.toContain(en.dashboard.chat.viewer.timeout);
    expect(buttonNames()).not.toContain(en.dashboard.chat.viewer.ban);
  });

  it('shows Timeout and Ban with community:read and moderation:ban', async () => {
    access.held = ['community:read', 'moderation:ban'];

    await show(ViewerPanel, panel);

    expect(buttonNames()).toContain(en.dashboard.chat.viewer.timeout);
    expect(buttonNames()).toContain(en.dashboard.chat.viewer.ban);
  });
});

describe('Chat identity panel', () => {
  it('shows the no-access note instead of the identity without chat:read', async () => {
    await show(ChatIdentityPanel, {});

    expect(document.body.querySelector('[data-test="no-access"]')).not.toBeNull();
    expect(document.body.querySelector('[data-test="preview"]')).toBeNull();
  });

  it('offers the scope re-grant only with twitch:diagnostics:read', async () => {
    access.held = ['chat:read'];
    await show(ChatIdentityPanel, {});

    expect(document.body.querySelector('[data-test="preview"]')).not.toBeNull();
    expect(document.body.querySelector('[data-test="regrant"]')).toBeNull();

    mounted?.unmount();
    access.held = ['chat:read', 'twitch:diagnostics:read'];
    await show(ChatIdentityPanel, {});

    expect(document.body.querySelector('[data-test="regrant"]')).not.toBeNull();
  });
});
