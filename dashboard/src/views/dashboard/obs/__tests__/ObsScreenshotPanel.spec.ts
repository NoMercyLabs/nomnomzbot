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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { Select } from '@/components/ui/select';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ObsScreenshotPanel from '../components/ObsScreenshotPanel.vue';

type Post = { path: string; body: unknown };

const posts: Post[] = [];

function listFor(path: string): object[] {
  if (path.endsWith('/obs/inputs')) {
    return [
      { name: 'Camera', kind: 'dshow_input' },
      { name: 'Desktop Audio', kind: 'wasapi_output_capture' },
    ];
  }

  return path.endsWith('/obs/scenes')
    ? [{ name: 'Main', isCurrent: true }]
    : [];
}

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => ({
      data: {
        data: listFor(path),
      },
      response: { status: 200, ok: true },
    }),
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return { data: { data: 'data:image/jpeg;base64,AAAA' }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function show(): VueWrapper {
  mounted = mount(ObsScreenshotPanel, {
    attachTo: document.body,
    global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }], i18n] },
    props: { offline: false },
  });

  return mounted;
}

describe('ObsScreenshotPanel', () => {
  // A source is picked from the inputs OBS reports, never typed by hand.
  it('captures the picked source in the chosen format and shows the returned image', async () => {
    const view = show();

    await flushPromises();
    view.findComponent(Select).vm.$emit('update:modelValue', 'Camera');
    await flushPromises();

    const jpg = view.findAll('[data-segmented] button').find((button) => button.text() === 'jpg');

    await jpg?.trigger('click');
    await view.get('[data-obs-shot-capture]').trigger('click');
    await flushPromises();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/source-screenshot',
        body: { sourceName: 'Camera', imageFormat: 'jpg' },
      },
    ]);

    expect(view.get('[data-obs-shot-image]').attributes('src')).toBe('data:image/jpeg;base64,AAAA');
  });

  // Stoney: "how the hell am i supposed to take a screenshot from an audio source?"
  it('lists scenes and video sources under group labels and offers no audio input', async () => {
    const view = show();

    await flushPromises();
    Element.prototype.hasPointerCapture = (): boolean => false;
    view.get('[data-obs-shot-source]').element.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true, button: 0 }));
    await flushPromises();

    const labels = [...document.body.querySelectorAll('[role="group"] > [role="presentation"], [role="group"] > div:first-child')];
    const text = document.body.textContent ?? '';

    expect(text).toContain(en.dashboard.obs.screenshot.groups.scenes);
    expect(text).toContain(en.dashboard.obs.screenshot.groups.captures);
    expect(text).toContain('Main');
    expect(text).toContain('Camera');
    expect(text).not.toContain('Desktop Audio');
    expect(labels.length).toBeGreaterThan(0);
  });

  it('holds Capture off, with the reason, until a source is picked', async () => {
    const view = show();

    await flushPromises();

    const capture = view.get('[data-obs-shot-capture]');

    expect((capture.element as HTMLButtonElement).disabled).toBe(true);
    expect(view.get('[data-obs-shot-hold]').attributes('title')).toBe(en.dashboard.obs.screenshot.pickFirst);

    await capture.trigger('click');
    await flushPromises();

    expect(posts).toHaveLength(0);
  });
});
