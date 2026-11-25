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

import { i18n } from '@/i18n';

import ThemeColorPanel from '../components/ThemeColorPanel.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

// The hex text box; the native color swatch is the other input of the picker.
const hexField = 'input:not([type="color"])';

type Put = { path: string; body: unknown };

const puts: Put[] = [];
let savedColor = '#112233';
let writable = true;

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
      data: { data: { color: savedColor } },
      response: { status: 200, ok: true },
    }),
    PUT: async (path: string, init: { body?: { color: string } }) => {
      puts.push({ path, body: init.body });
      savedColor = init.body?.color ?? '';

      return {
        data: { data: { color: savedColor } },
        response: { status: 200, ok: true },
      };
    },
  },
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  puts.length = 0;
  savedColor = '#112233';
  writable = true;
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('nl', nl);
  i18n.global.locale.value = 'en';
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<VueWrapper> {
  mounted = mount(ThemeColorPanel, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  await flushPromises();

  return mounted;
}

describe('ThemeColorPanel', () => {
  it('shows the saved color under the label, in both languages', async () => {
    const wrapper = await open();

    expect(wrapper.text()).toContain('Theme color for all overlays');
    expect(wrapper.find<HTMLInputElement>(hexField).element.value).toBe('#112233');
    expect(nl.dashboard.overlays.themeColor.label).not.toBe(en.dashboard.overlays.themeColor.label);
    expect(nl.dashboard.overlays.themeColor.reset).not.toBe(en.dashboard.overlays.themeColor.reset);
  });

  it('saves a typed color through the one write', async () => {
    const wrapper = await open();
    const field = wrapper.find<HTMLInputElement>(hexField);

    await field.setValue('#aabbcc');
    await new Promise((done) => setTimeout(done, 500));
    await flushPromises();

    expect(puts).toEqual([{ path: '/api/v{version}/overlay-theme-color', body: { color: '#AABBCC' } }]);
  });

  it('clears the key when the reset button is pressed', async () => {
    const wrapper = await open();
    const reset = wrapper.findAll('button').find((b) => b.text() === 'Reset to the theme\'s own color');

    expect(reset).toBeDefined();

    await reset?.trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: '/api/v{version}/overlay-theme-color', body: { color: '' } }]);
    expect(wrapper.find<HTMLInputElement>(hexField).element.value).toBe('');
  });

  it('has nothing to reset when no color is set, and a read-only viewer cannot reset', async () => {
    savedColor = '';
    const empty = await open();

    expect(empty.findAll('button').find((b) => b.text().startsWith('Reset'))?.attributes('disabled')).toBeDefined();
    empty.unmount();

    savedColor = '#112233';
    writable = false;
    const readOnly = await open();

    expect(readOnly.findAll('button').find((b) => b.text().startsWith('Reset'))?.attributes('disabled')).toBeDefined();
  });
});
