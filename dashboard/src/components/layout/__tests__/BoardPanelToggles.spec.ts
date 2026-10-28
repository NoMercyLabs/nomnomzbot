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
import { afterEach, describe, expect, it, vi } from 'vitest';

import { boardToggles } from '@/components/layout/board';
import BoardPanelToggles from '@/components/layout/BoardPanelToggles.vue';
import { i18n } from '@/i18n';

describe('BoardPanelToggles', () => {
  afterEach(() => {
    boardToggles.value = [];
  });

  it('keeps one switch per optional panel from md and folds them into one Panels menu below md', () => {
    const set = vi.fn<(shown: boolean) => void>();

    boardToggles.value = [
      {
        id: 'info',
        label: 'What viewers see',
        shown: true,
        set,
      },
      {
        id: 'platforms',
        label: 'Where it goes',
        shown: false,
        set,
      },
    ];

    const wrapper = mount(BoardPanelToggles, { global: { plugins: [i18n] } });
    const wide = wrapper.get('[data-board-toggles-wide]');
    const menu = wrapper.get('[data-board-toggles-menu]');

    expect(wide.classes()).toEqual(expect.arrayContaining(['max-md:hidden', 'contents']));

    expect(wide.findAll('[data-board-toggle]').map((node) => node.attributes('data-board-toggle'))).toEqual([
      'info',
      'platforms',
    ]);

    expect(menu.classes()).toContain('md:hidden');
    expect(menu.text()).toContain('Panels');
  });

  it('draws nothing for a page with no optional panel', () => {
    const wrapper = mount(BoardPanelToggles, { global: { plugins: [i18n] } });

    expect(wrapper.find('[data-board-toggles-menu]').exists()).toBe(false);
    expect(wrapper.find('[data-board-toggles-wide]').exists()).toBe(false);
  });
});
