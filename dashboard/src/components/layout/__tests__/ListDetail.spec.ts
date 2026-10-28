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

import ListDetail from '@/components/layout/ListDetail.vue';

function detailClasses(detailIdle: boolean | undefined): string {
  return mount(ListDetail, {
    props: { detailIdle },
    slots: { list: '<div>list</div>', detail: '<div>detail</div>' },
  })
    .get('[data-slot="list-detail-detail"]')
    .classes()
    .join(' ');
}

describe('the list and detail layout', () => {
  it('stretches the last detail panel to the full column height by default', () => {
    const classes = detailClasses(undefined);

    expect(classes).toContain('md:*:last:flex-1');
    expect(classes).not.toContain('md:*:last:flex-none');
  });

  it('keeps the detail panel at its own height while nothing is open (detailIdle)', () => {
    const classes: string = detailClasses(true);

    expect(classes).toContain('md:*:last:flex-none');
    expect(classes).not.toContain('md:*:last:flex-1');
    expect(classes).not.toContain('md:[&>:last-child>[data-slot=panel-card]:last-child]:flex-1');
  });
});
