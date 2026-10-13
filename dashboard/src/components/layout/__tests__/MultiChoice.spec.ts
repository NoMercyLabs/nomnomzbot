// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import MultiChoice from '../MultiChoice.vue';

const options = [
  { value: 'title', label: 'Song title' },
  { value: 'artist', label: 'Artist' },
] as const;

describe('MultiChoice', () => {
  it('adds a pressed option to the chosen list and keeps the others', async () => {
    const wrapper: VueWrapper = mount(MultiChoice, { props: { options, modelValue: ['title'] } });

    await wrapper.findAll('button')[1]!.trigger('click');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([['title', 'artist']]);
  });

  it('lets the last chosen option go, so none chosen is allowed', async () => {
    const wrapper: VueWrapper = mount(MultiChoice, { props: { options, modelValue: ['title'] } });

    await wrapper.findAll('button')[0]!.trigger('click');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([[]]);
  });

  it('marks each option on or off from the chosen list', () => {
    const wrapper: VueWrapper = mount(MultiChoice, { props: { options, modelValue: ['artist'] } });

    expect(wrapper.findAll('button').map((b) => [b.text(), b.attributes('data-state')])).toEqual([
      ['Song title', 'off'],
      ['Artist', 'on'],
    ]);
  });
});
