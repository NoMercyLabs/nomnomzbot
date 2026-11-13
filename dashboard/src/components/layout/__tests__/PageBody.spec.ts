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

  // The scrollbar takes its own width from the right edge. Reserving the gutter and shrinking the right padding by
  // that width keeps the content inset equal on the left and right, with or without a scrollbar.
  it('reserves the scrollbar gutter and takes its width off the right padding', () => {
    const body = mount(PageBody);
    const classes = body.classes();

    expect(classes).toContain('scrollbar-gutter-stable');
    expect(classes).toContain('pr-[calc(1.5rem-var(--scrollbar-width))]');
  });

  // A list page's lead panel fills the height and scrolls inside itself, so the dense body has no scroll of its own.
  it('keeps a dense body at gap-3 with no scroll and no shrink guard', () => {
    const body = mount(PageBody, { props: { dense: true } });
    const classes = body.classes();

    expect(body.attributes('data-slot')).toBe('page-body');
    expect(classes).toContain('gap-3');
    expect(classes).toContain('flex-1');
    expect(classes).not.toContain('overflow-y-auto');
    expect(classes).not.toContain('*:shrink-0');
  });
});
