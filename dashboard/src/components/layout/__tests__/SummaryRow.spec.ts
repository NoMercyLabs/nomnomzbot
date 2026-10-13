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

import SummaryRow from '@/components/layout/SummaryRow.vue';

const startGhost = '[&>[data-variant=ghost]:first-child]:-ml-2.5';
const endGhost = '[&>[data-variant=ghost]:last-child]:-mr-2.5';

function mountRow(slots: Record<string, string> = {}): VueWrapper {
  return mount(SummaryRow, {
    props: { title: 'Twitch' },
    slots,
  });
}

function pill(wrapper: VueWrapper): string {
  return wrapper.find('[data-slot="summary-row-body"]').classes().join(' ');
}

describe('summary row visual balance', () => {
  it('gives text with no icon and no button the same 12px inset on both sides', () => {
    const wrapper = mountRow();

    expect(pill(wrapper)).toContain('px-3');
    expect(wrapper.find('[data-slot="summary-row-trailing"]').exists()).toBe(false);
  });

  it('keeps the start icon inside the 12px inset', () => {
    const wrapper = mountRow({ leading: '<svg data-test="icon" />' });
    const body = wrapper.find('[data-slot="summary-row-body"]');

    expect(pill(wrapper)).toContain('px-3');
    expect(body.element.firstElementChild?.getAttribute('data-test')).toBe('icon');
  });

  it('keeps the end icon inside the 12px inset', () => {
    const wrapper = mountRow({ end: '<svg data-test="icon" />' });
    const body = wrapper.find('[data-slot="summary-row-body"]');

    expect(pill(wrapper)).toContain('px-3');
    expect(body.element.lastElementChild?.getAttribute('data-test')).toBe('icon');
  });

  it('pulls a ghost start button left by its own padding so its ink meets the inset', () => {
    const wrapper = mountRow({ leading: '<button data-variant="ghost">Go</button>' });

    expect(pill(wrapper)).toContain(startGhost);
  });

  it('pulls a ghost end button right by its own padding so its ink meets the inset', () => {
    const wrapper = mountRow({ end: '<button data-variant="ghost">Go</button>' });

    expect(pill(wrapper)).toContain(endGhost);
  });

  it('gives a trailing button outside the body the 12px right inset, less its ghost padding', () => {
    const wrapper = mountRow({ trailing: '<button data-variant="ghost">Go</button>' });
    const trailing = wrapper.find('[data-slot="summary-row-trailing"]');

    expect(trailing.classes()).toContain('pr-3');
    expect(trailing.classes()).toContain('[&_[data-variant=ghost]]:-mr-2.5');
    expect(trailing.classes()).toContain('[&_[data-variant=ghost][data-size^=icon]]:-mr-2');
  });
});
