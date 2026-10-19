// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import StatStrip from '../StatStrip.vue';

describe('StatStrip', () => {
  it('lets a lone last tile on a phone span both columns, so no tile sits alone at half width', () => {
    const strip = mount(StatStrip);
    const classes: string[] = strip.get('[data-slot="stat-strip"]').classes();

    expect(classes).toContain('max-sm:[&>:last-child:nth-child(odd)]:col-span-2');
  });

  it('keeps the wide layout one auto-fit row', () => {
    const strip = mount(StatStrip);

    expect(strip.get('[data-slot="stat-strip"]').classes()).toContain(
      'grid-cols-[repeat(auto-fit,minmax(10rem,1fr))]',
    );
  });
});
