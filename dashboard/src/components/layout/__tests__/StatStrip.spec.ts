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

const STACK_CLASS = 'max-sm:[&:has(>:last-child:nth-child(3))]:grid-cols-1';
const SPAN_CLASS = 'max-sm:[&>:last-child:nth-child(odd):not(:nth-child(3))]:col-span-2';
const STACK_SELECTOR = ':has(>:last-child:nth-child(3))';
const SPAN_SELECTOR = ':last-child:nth-child(odd):not(:nth-child(3))';

function mountStrip(count: number): HTMLElement {
  const tiles = '<i></i>'.repeat(count);
  const strip = mount(StatStrip, { slots: { default: tiles } });

  return strip.element as HTMLElement;
}

describe('StatStrip', () => {
  it('carries both phone rules and the wide auto-fit row', () => {
    const classes: string[] = mountStrip(3).classList.value.split(' ');

    expect(classes).toContain(STACK_CLASS);
    expect(classes).toContain(SPAN_CLASS);
    expect(classes).toContain('grid-cols-[repeat(auto-fit,minmax(10rem,1fr))]');
  });

  it('stacks exactly three tiles in one column and never spans the third into a second column', () => {
    const strip = mountStrip(3);
    const third = strip.children[2] as HTMLElement;

    expect(strip.matches(STACK_SELECTOR)).toBe(true);
    expect(third.matches(SPAN_SELECTOR)).toBe(false);
  });

  it.each([1, 2, 4, 5])('does not stack %i tiles', (count) => {
    expect(mountStrip(count).matches(STACK_SELECTOR)).toBe(false);
  });

  it.each([1, 5])('spans the lone last of %i tiles across the phone row', (count) => {
    const strip = mountStrip(count);
    const last = strip.children[count - 1] as HTMLElement;

    expect(last.matches(SPAN_SELECTOR)).toBe(true);
  });

  it.each([2, 4])('spans nothing for %i tiles, which pair evenly', (count) => {
    const strip = mountStrip(count);
    const last = strip.children[count - 1] as HTMLElement;

    expect(last.matches(SPAN_SELECTOR)).toBe(false);
  });
});
