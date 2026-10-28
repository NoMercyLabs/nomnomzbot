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

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';

import EventKindMask from '../EventKindMask.vue';

const kinds = [
  {
    name: 'Scenes',
    bit: 4,
    onByDefault: true,
  },
  {
    name: 'Inputs',
    bit: 8,
    onByDefault: true,
  },
  {
    name: 'InputVolumeMeters',
    bit: 65536,
    onByDefault: false,
  },
] as const;

function mountMask(modelValue: number, disabled = false): VueWrapper {
  return mount(EventKindMask, {
    props: {
      kinds,
      modelValue,
      prefix: 'dashboard.obs.kinds',
      disabled,
    },
    global: { plugins: [i18n] },
  });
}

describe('EventKindMask', () => {
  it('ticks exactly the kinds whose bit is in the mask', () => {
    const wrapper = mountMask(4 | 65536);

    const state = wrapper.findAll('[data-kind]').map((box) => [
      box.attributes('data-kind'),
      box.attributes('data-state'),
    ]);

    expect(state).toEqual([
      ['Scenes', 'checked'],
      ['Inputs', 'unchecked'],
      ['InputVolumeMeters', 'checked'],
    ]);
  });

  it('adds the bit of a ticked kind and keeps the other bits', async () => {
    const wrapper = mountMask(4);

    await wrapper.find('[data-kind="Inputs"]').trigger('click');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([4 | 8]);
  });

  it('removes only the bit of an unticked kind', async () => {
    const wrapper = mountMask(4 | 8);

    await wrapper.find('[data-kind="Scenes"]').trigger('click');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([8]);
  });

  it('resets to the kinds the server marks on by default', async () => {
    const wrapper = mountMask(65536);

    await wrapper.find('[data-kind-reset]').trigger('click');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([4 | 8]);
  });

  it('changes nothing when disabled', async () => {
    const wrapper: VueWrapper = mountMask(4, true);

    await wrapper.find('[data-kind="Inputs"]').trigger('click');

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });
});
