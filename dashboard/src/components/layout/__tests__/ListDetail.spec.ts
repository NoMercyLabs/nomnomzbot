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
