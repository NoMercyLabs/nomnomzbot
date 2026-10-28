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

import RowText from '@/components/layout/RowText.vue';

describe('the text of a setting row', () => {
  it('puts the hint on a line of its own, so a long title never wraps because of it', () => {
    const wrapper = mount(RowText, {
      props: {
        title: 'Content popularity and growth',
        description: 'How much a viewer counts.',
        hint: 'Default 0.4',
      },
    });
    const title = wrapper.get('[data-row-title]').element;
    const hint = wrapper.get('[data-row-hint]').element;

    expect(title.parentElement?.classList.contains('flex')).toBe(false);
    expect(title.nextElementSibling).not.toBe(hint);
    expect(hint.classList.contains('block')).toBe(true);
    expect(hint.textContent).toBe('Default 0.4');
  });

  it('shows no hint line when the row has no hint', () => {
    const wrapper = mount(RowText, { props: { title: 'Read aloud' } });

    expect(wrapper.find('[data-row-hint]').exists()).toBe(false);
  });
});
