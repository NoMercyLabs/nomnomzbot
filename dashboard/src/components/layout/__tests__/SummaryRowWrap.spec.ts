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

import SummaryRow from '@/components/layout/SummaryRow.vue';

// Stoney 2026-10-18: queue titles cut to "Fuck iT ! I c..." with the buttons crowding the text. A wrapping row
// moves its buttons to a second line before the body shrinks below 20rem.
describe('a summary row that wraps its trailing buttons', () => {
  const slots = { trailing: '<button>Play next</button>' };

  it('wraps, keeps a 20rem body and pushes the buttons to the end of the second line', () => {
    const row = mount(SummaryRow, { props: { title: 'Song', wrap: true }, slots });

    expect(row.classes()).toContain('flex-wrap');
    expect(row.find('[data-slot="summary-row-body"]').classes()).toContain('flex-[1_1_20rem]');
    expect(row.find('[data-slot="summary-row-trailing"]').classes()).toContain('ml-auto');
  });

  it('leaves every other row as it was: no wrap, a flexible body', () => {
    const row = mount(SummaryRow, { props: { title: 'Song' }, slots });

    expect(row.classes()).not.toContain('flex-wrap');
    expect(row.find('[data-slot="summary-row-body"]').classes()).toContain('flex-1');
    expect(row.find('[data-slot="summary-row-trailing"]').classes()).not.toContain('ml-auto');
  });
});
