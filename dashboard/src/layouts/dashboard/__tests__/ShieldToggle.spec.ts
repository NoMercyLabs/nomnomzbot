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

import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ShieldToggle from '../ShieldToggle.vue';

const { mutate, narrow } = vi.hoisted(() => ({
  mutate: vi.fn<(on: boolean) => void>(),
  narrow: { value: false },
}));

vi.mock('@/api/shield', () => ({
  useShieldMode: () => ({ data: ref(false) }),
  useSetShieldMode: () => ({ mutate, isPending: ref(false) }),
}));

vi.mock('@/composables/useLayout', () => ({ useNarrowScreen: () => narrow }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ displayName: 'Streamer' }) }),
}));

describe('the Shield Mode toggle', () => {
  it('is a 36px control (h-9), not the 32px small one', () => {
    const classes: string = mount(ShieldToggle, { global: { plugins: [i18n] } })
      .get('[data-slot="shield-toggle"]')
      .classes()
      .join(' ');

    expect(classes).toContain('h-9');
    expect(classes).not.toContain('h-8');
  });

  it('draws a 20px icon (size-5) inside the 36px control', () => {
    const icon = mount(ShieldToggle, { global: { plugins: [i18n] } }).get(
      '[data-slot="shield-toggle"] svg',
    );

    expect(icon.classes()).toContain('size-5');
  });

  it('writes at once on a wide screen', async () => {
    narrow.value = false;
    mutate.mockClear();
    const wrapper = mount(ShieldToggle, { global: { plugins: [i18n] } });

    await wrapper.get('[data-slot="shield-toggle"]').trigger('click');

    expect(mutate).toHaveBeenCalledExactlyOnceWith(true);
  });

  it('asks first on a phone, names the effect, and writes only after the confirm', async () => {
    narrow.value = true;
    mutate.mockClear();
    const wrapper = mount(ShieldToggle, { global: { plugins: [i18n] }, attachTo: document.body });

    await wrapper.get('[data-slot="shield-toggle"]').trigger('click');
    await flushPromises();

    expect(mutate).not.toHaveBeenCalled();
    expect(document.body.textContent).toContain('Turn Shield mode on for Streamer?');

    document.body.querySelector<HTMLElement>('[data-confirm-action]')?.click();
    await flushPromises();

    expect(mutate).toHaveBeenCalledExactlyOnceWith(true);
    wrapper.unmount();
  });
});
