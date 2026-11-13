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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import PublicWait from '../PublicWait.vue';

const pages = import.meta.glob<string>('../../**/*.vue', {
  eager: true,
  query: '?raw',
  import: 'default',
});

// A hand-written wait line: a status paragraph that shows while something loads, checks or waits.
const waitLine = /<p v-(?:else-)?if="[^"]*(?:[Pp]ending|[Ll]oading|checking|waiting|isLoaded|!code)[^"]*"[^>]*role="status"/;

describe('the public wait line', () => {
  it('shows a spinner beside the words and announces them once', () => {
    const wrapper = mount(PublicWait, { slots: { default: 'Loading the sign-in options' } });
    const status = wrapper.findAll('[role="status"]');

    expect(status).toHaveLength(1);
    expect(status[0]!.text()).toBe('Loading the sign-in options');
    expect(wrapper.find('svg.animate-spin').exists()).toBe(true);
    expect(wrapper.find('svg.animate-spin').attributes('aria-hidden')).toBe('true');
  });

  // A wait on a public page (sign in, invite, setup) draws one spinner and the words through PublicWait, so a
  // long step never reads as a frozen page and no page keeps its own plain-text wait line.
  it('is the only wait line a public page draws', () => {
    const own = Object.entries(pages)
      .filter(([path]) => !path.endsWith('/PublicWait.vue'))
      .filter(([, source]) => waitLine.test(source))
      .map(([path]) => path);

    expect(own).toEqual([]);
  });
});
