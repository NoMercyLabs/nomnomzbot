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

import SearchIcon from '@/components/layout/SearchIcon.vue';

describe('the search icon', () => {
  it('draws a 2px line at 20px: stroke 24 / 20 * 2 = 2.4, sized size-5', () => {
    const icon: string = mount(SearchIcon, { props: { size: 5 } }).find('svg').attributes('class') ?? '';

    expect(icon).toContain('[&_path]:stroke-[2.4]');
    expect(icon).toContain('size-5');
  });

  it('draws a 2px line at 16px: stroke 3, left at the input-group size', () => {
    const icon: string = mount(SearchIcon).find('svg').attributes('class') ?? '';

    expect(icon).toContain('[&_path]:stroke-3');
    expect(icon).not.toContain('size-5');
  });
});
