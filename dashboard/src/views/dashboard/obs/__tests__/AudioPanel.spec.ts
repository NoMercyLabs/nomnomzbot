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

import { obsInputsKey, type ObsInput } from '@/api/obs';
import { i18n } from '@/i18n';

import AudioPanel from '../components/AudioPanel.vue';
import en from '../i18n/en.json';

type Post = { path: string; body: unknown };

const posts: Post[] = [];
const toasts: string[] = [];
let refuse: boolean = false;

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('vue-sonner', () => ({
  toast: { error: (message: string) => toasts.push(message) },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async () => ({
      data: {
        data: [
          {
            name: 'Mic',
            kind: 'wasapi_input_capture',
            muted: false,
            volumeDb: -6,
          },
          {
            name: 'Logo',
            kind: 'image_source',
            muted: null,
            volumeDb: null,
          },
        ],
      },
      response: { status: 200, ok: true },
    }),
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return refuse
        ? { error: { message: 'OBS said no' }, response: { status: 500, ok: false } }
        : { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

let mounted: VueWrapper | null = null;
let queries: QueryClient;

beforeEach(() => {
  posts.length = 0;
  toasts.length = 0;
  refuse = false;
  queries = new QueryClient();
  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal('ResizeObserver', class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
});

async function show(): Promise<void> {
  mounted = mount(AudioPanel, {
    attachTo: document.body,
    props: { offline: false },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

function cached(): ObsInput[] | undefined {
  return queries.getQueryData<ObsInput[]>(obsInputsKey('c1'));
}

describe('AudioPanel', () => {
  it('lists only inputs that carry audio, with the dB level', async () => {
    await show();

    const rows = document.body.querySelectorAll('[data-obs-input]');

    expect(rows).toHaveLength(1);
    expect(rows[0]?.getAttribute('data-obs-input')).toBe('Mic');
    expect(rows[0]?.textContent).toContain('-6.0 dB');
  });

  it('shows the offline text while the OBS link is off', async () => {
    mounted = mount(AudioPanel, {
      attachTo: document.body,
      props: { offline: true },
      global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
    });

    await flushPromises();

    expect(document.body.querySelector('[data-obs-input]')).toBeNull();
    expect(document.body.textContent).toContain(en.dashboard.obs.audio.offline);
  });

  it('the mute button posts the new state and flips the row at once', async () => {
    await show();

    document.body.querySelector<HTMLButtonElement>('[data-obs-mute="Mic"]')?.click();
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/inputs/mute',
        body: {
          inputName: 'Mic',
          muted: true,
        },
      },
    ]);

    expect(cached()?.[0]?.muted).toBe(true);

    expect(
      document.body.querySelector('[data-obs-mute="Mic"]')?.getAttribute('aria-pressed'),
    ).toBe('true');
  });

  it('a refused mute rolls the row back and toasts the error', async () => {
    await show();

    refuse = true;
    document.body.querySelector<HTMLButtonElement>('[data-obs-mute="Mic"]')?.click();
    await flushPromises();

    expect(cached()?.[0]?.muted).toBe(false);
    expect(toasts).toHaveLength(1);
  });

  it('a burst of slider moves posts one volume write with the last value', async () => {
    vi.useFakeTimers();
    await show();

    const slider = mounted?.findComponent({ name: 'SliderRoot' });

    slider?.vm.$emit('update:modelValue', [-12]);
    slider?.vm.$emit('update:modelValue', [-18]);
    slider?.vm.$emit('update:modelValue', [-24]);
    await vi.advanceTimersByTimeAsync(400);
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/inputs/volume',
        body: {
          inputName: 'Mic',
          volumeDb: -24,
        },
      },
    ]);

    expect(cached()?.[0]?.volumeDb).toBe(-24);
  });

  it('a refused volume write puts the old level back and toasts', async () => {
    vi.useFakeTimers();
    await show();

    refuse = true;
    mounted?.findComponent({ name: 'SliderRoot' }).vm.$emit('update:modelValue', [-30]);
    await vi.advanceTimersByTimeAsync(400);
    await flushPromises();

    expect(cached()?.[0]?.volumeDb).toBe(-6);
    expect(toasts).toHaveLength(1);
  });
});
