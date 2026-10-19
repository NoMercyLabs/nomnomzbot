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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import AudioMixPanel from '../components/AudioMixPanel.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

type Put = { path: string; body: unknown };

const puts: Put[] = [];
let refuseWith: string | null = null;
let writable: boolean = true;

const stored = {
  masterVolume: 80,
  ttsVolume: 60,
  defaultMasterVolume: 100,
  defaultTtsVolume: 100,
  minVolume: 0,
  maxVolume: 100,
};

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => writable }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: { data: stored },
      response: { status: 200, ok: true },
    }),
    PUT: async (path: string, init: { body?: unknown }) => {
      puts.push({ path, body: init.body });

      return refuseWith === null
        ? {
            data: { data: { ...stored, ...(init.body as object) } },
            response: { status: 200, ok: true },
          }
        : { error: { message: refuseWith }, response: { status: 400, ok: false } };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  puts.length = 0;
  refuseWith = null;
  writable = true;
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('nl', nl);
  i18n.global.locale.value = 'en';

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.unstubAllGlobals();
});

async function open(): Promise<VueWrapper> {
  mounted = mount(AudioMixPanel, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  await flushPromises();

  return mounted;
}

async function type(wrapper: VueWrapper, field: string, value: string): Promise<void> {
  const input = wrapper.find(`input[data-audio-mix="${field}"]`);

  await input.setValue(value);
  await input.trigger('blur');
  await flushPromises();
}

describe('AudioMixPanel', () => {
  it('shows the saved volumes and names the default the server sent', async () => {
    const wrapper = await open();

    const master = wrapper.find<HTMLInputElement>('input[data-audio-mix="masterVolume"]');
    const tts = wrapper.find<HTMLInputElement>('input[data-audio-mix="ttsVolume"]');

    expect(master.element.value).toBe('80');
    expect(tts.element.value).toBe('60');

    expect(wrapper.findAll('[data-row-hint]').map((hint) => hint.text())).toEqual([
      'Default 100',
      'Default 100',
    ]);
  });

  it('names each slider thumb, not only the slider root', async () => {
    const wrapper = await open();

    const names = wrapper
      .findAll('[role="slider"]')
      .map((thumb) => thumb.attributes('aria-label'));

    expect(names).toEqual(['Master volume', 'Voice volume']);
  });

  it('sends both volumes in one write when one field changes', async () => {
    const wrapper = await open();

    await type(wrapper, 'masterVolume', '40');

    expect(puts).toEqual([
      { path: '/api/v{version}/audio-mix', body: { masterVolume: 40, ttsVolume: 60 } },
    ]);
  });

  it('keeps a typed value inside the limits the server sent', async () => {
    const wrapper = await open();

    await type(wrapper, 'ttsVolume', '150');

    expect(puts[0]?.body).toEqual({ masterVolume: 80, ttsVolume: 100 });
  });

  it('shows the translated server refusal on the field it names and nowhere else', async () => {
    refuseWith = 'audio_mix_tts_volume_out_of_range';

    const wrapper = await open();

    await type(wrapper, 'ttsVolume', '30');

    const shown = wrapper.find('[data-audio-mix-error="ttsVolume"]');

    const wanted = en.dashboard.overlays.audioMix.errors.audio_mix_tts_volume_out_of_range;

    expect(shown.text()).toBe(wanted);
    expect(wrapper.find('[data-audio-mix-error="masterVolume"]').exists()).toBe(false);
  });

  it('has a Dutch text for both refusal keys', () => {
    const keys = Object.keys(en.dashboard.overlays.audioMix.errors);

    expect(keys.sort()).toEqual([
      'audio_mix_master_volume_out_of_range',
      'audio_mix_tts_volume_out_of_range',
    ]);

    expect(Object.keys(nl.dashboard.overlays.audioMix.errors).sort()).toEqual(keys.sort());
  });

  it('cannot be edited without the permission to write', async () => {
    writable = false;

    const wrapper = await open();

    expect(wrapper.find('input[data-audio-mix="masterVolume"]').attributes('disabled')).toBeDefined();
    expect(wrapper.find('input[data-audio-mix="ttsVolume"]').attributes('disabled')).toBeDefined();
  });
});
