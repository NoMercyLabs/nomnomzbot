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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref, type Component } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import LexiconPanel from '../LexiconPanel.vue';
import PlaySampleButton from '../PlaySampleButton.vue';

const held = vi.hoisted(() => ({ keys: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: {
        data: [
          {
            id: 'e1',
            phrase: 'gg',
            replacement: 'good game',
            matchKind: 'word',
          },
        ],
      },
      response: { status: 200, ok: true },
    }),
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(
  component: Component,
  keys: string[],
  props: Record<string, unknown> = {},
): Promise<VueWrapper> {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(component, {
    attachTo: document.body,
    props,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

describe('TTS shows only what the person may use', () => {
  it('lists the lexicon without the add button or row actions with tts:config:read alone', async () => {
    const view = await open(LexiconPanel, ['tts:config:read']);

    expect(view.text()).toContain('good game');
    expect(view.find('[data-lexicon-add]').exists()).toBe(false);
    expect(view.find('[aria-label^="Edit"]').exists()).toBe(false);
  });

  it('shows the add button and row actions with tts:config:write', async () => {
    const view = await open(LexiconPanel, ['tts:config:read', 'tts:config:write']);

    expect(view.find('[data-lexicon-add]').exists()).toBe(true);
    expect(view.findAll('button[aria-label]').length).toBeGreaterThanOrEqual(2);
  });

  it('shows the voice sample button only with tts:voice:test', async () => {
    const without = await open(PlaySampleButton, [], { voiceId: 'v1' });

    expect(without.find('[data-voice-play]').exists()).toBe(false);

    without.unmount();
    mounted = null;

    const tester = await open(PlaySampleButton, ['tts:voice:test'], { voiceId: 'v1' });

    expect(tester.find('[data-voice-play]').exists()).toBe(true);
  });
});
