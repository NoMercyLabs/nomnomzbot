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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';

import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('CopyLinkButton', () => {
  it('puts the link on the clipboard, says Copied, then turns back', async () => {
    vi.useFakeTimers();
    const writeText = vi.fn<(text: string) => Promise<void>>(async () => undefined);

    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

    mounted = mount(CopyLinkButton, {
      props: { url: 'https://bot.example/popout/chat', label: 'Copy dock link' },
      global: { plugins: [i18n] },
    });

    await flushPromises();
    expect(mounted.text()).toBe('Copy dock link');

    await mounted.find('button').trigger('click');
    await flushPromises();

    expect(writeText).toHaveBeenCalledWith('https://bot.example/popout/chat');
    expect(mounted.text()).toBe('Copied');

    vi.advanceTimersByTime(2000);
    await flushPromises();

    expect(mounted.text()).toBe('Copy dock link');
  });

  it('is a 36px button with a 20px icon, like the icon actions beside it', () => {
    mounted = mount(CopyLinkButton, {
      props: { url: 'https://bot.example/x', label: 'Copy link' },
      global: { plugins: [i18n] },
    });

    const button = mounted.get('[data-copy-link]');

    expect(button.classes()).toContain('h-9');
    expect(button.classes()).not.toContain('h-8');
    expect(button.get('svg').classes()).toContain('size-5');
  });
});
