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

import PageBody from '@/components/layout/PageBody.vue';

describe('the page body', () => {
  // PanelCard is min-h-0 + overflow-hidden, so in a scrolling flex column it shrinks to its title row unless the
  // body forbids it. The rule must hold at every width, not only on a phone.
  it('keeps every child at its content height at every width', () => {
    const body = mount(PageBody);
    const classes = body.classes();

    expect(classes).toContain('*:shrink-0');
    expect(classes).not.toContain('max-md:*:shrink-0');
  });
});
