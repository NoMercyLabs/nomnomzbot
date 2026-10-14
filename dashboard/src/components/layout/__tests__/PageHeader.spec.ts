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

import PageHeader from '../PageHeader.vue';

describe('PageHeader actions group', () => {
  const wrapper = mount(PageHeader, {
    props: { title: 'Music' },
    slots: { actions: '<button>Go</button>' },
  });
  const group = wrapper.find('[data-slot="page-actions"]');
  const classes = group.classes();

  it('is only pushed to the right edge once the header is wide enough for one row', () => {
    expect(classes).toContain('@2xl:ml-auto');
    expect(classes).not.toContain('ml-auto');
  });

  it('fills its own row from the left when the header wraps on a phone', () => {
    expect(classes).toContain('w-full');
    expect(classes).toContain('flex-wrap');
    expect(classes).toContain('@2xl:w-auto');
  });
});
