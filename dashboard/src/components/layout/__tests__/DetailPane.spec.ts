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

import DetailPane from '@/components/layout/DetailPane.vue';

describe('the detail pane', () => {
  it('keeps the header out of the part that scrolls, so the title and actions stay in view', () => {
    const pane: VueWrapper = mount(DetailPane, {
      slots: { header: '<h2 id="title">Alerts</h2>', default: '<p id="body">Health</p>' },
    });

    const body: Element = pane.get('[data-slot="detail-pane-body"]').element;

    expect(body.className).toContain('overflow-y-auto');
    expect(body.contains(pane.get('#body').element)).toBe(true);
    expect(body.contains(pane.get('#title').element)).toBe(false);

    expect(pane.get('[data-slot="detail-pane"]').element.className).not.toContain(
      'overflow-y-auto',
    );
  });
});
