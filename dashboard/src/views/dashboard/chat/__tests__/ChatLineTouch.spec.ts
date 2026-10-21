// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Stoney 2026-10-21: "some content is cut off or super small, what works on desktop does not work for mobile".
// A name, badge or emote is small by design on a chat line; on touch its hit area grows to 36 px with an
// absolute ::after, so the line keeps its height and desktop stays the same.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
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
  useChannelAccess: () => ({ known: { value: true }, can: (): boolean => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1'), current: computed(() => null) }),
}));

const css = readFileSync(resolve(__dirname, '../../../../styles/main.css'), 'utf8');
const utility = /@utility touch-hit \{([\s\S]*?)\n\}/.exec(css)?.[1] ?? '';

const url = `https://example.com/${'a'.repeat(100)}`;

const chatter = {
  id: 'm1',
  channelId: 'c1',
  userId: 'u1',
  displayName: 'DukaSoft',
  username: 'dukasoft',
  message: url,
  fragments: [
    {
      type: 'text',
      text: url,
    },
    {
      type: 'text',
      text: 'Kappa',
      emote: {
        id: '25',
        name: 'Kappa',
        provider: 'twitch',
        urls: { '1x': 'https://cdn.example/kappa.png' },
      },
    },
  ],
  badges: [
    {
      setId: 'moderator',
      id: '1',
      urls: { '1x': 'https://cdn.example/mod.png' },
      title: 'Moderator',
    },
  ],
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

async function show(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  const host = defineComponent({
    render: () => h(TooltipProvider, null, () =>
      h(ChatLine, {
        line: chatter,
        channelId: 'c1',
        display: chatDisplayDefaults,
        index: 0,
        me: 'kitte',
      })),
  });

  mounted = mount(host, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

describe('the touch hit area of a chat line', () => {
  it('defines a touch-hit utility that grows to 36 px only on a coarse pointer', () => {
    expect(utility).toContain('pointer: coarse');
    expect(utility).toContain('::after');
    expect(utility).toContain('position: absolute');
    expect(utility).toContain('2.25rem');
  });

  it('gives the name and the emote of a line the touch hit area', async () => {
    await show();

    const targets = [
      ...document.body.querySelectorAll('[data-slot="user-name"], [data-asset-trigger]'),
    ];

    expect(targets.length).toBeGreaterThanOrEqual(2);
    expect(targets.every((item) => item.classList.contains('touch-hit'))).toBe(true);
  });

  it('wraps a 120-character link inside the line, so it never passes the right edge', async () => {
    await show();

    const text = [...document.body.querySelectorAll('p')].find((item) =>
      item.textContent?.includes(url));

    expect(url).toHaveLength(120);
    expect(text?.classList.contains('wrap-anywhere')).toBe(true);
    expect(document.body.querySelector('[data-line-id]')?.querySelector('.min-w-0')).not.toBeNull();
  });
});
