// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
