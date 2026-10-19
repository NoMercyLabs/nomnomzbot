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
