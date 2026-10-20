// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { h } from 'vue';

import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '..';

async function openMenu() {
  const wrapper = mount(
    {
      render: () =>
        h(DropdownMenu, null, () => [
          h(DropdownMenuTrigger, { 'data-trigger': '' }, () => 'Open'),
          h(DropdownMenuContent, null, () =>
            h(DropdownMenuItem, null, () => 'Chat identity and everything else'),
          ),
        ]),
    },
    { attachTo: document.body },
  );

  await wrapper.get('[data-trigger]').trigger('keydown', { key: 'Enter' });
  await flushPromises();
}

describe('DropdownMenuContent', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('sizes the menu to its longest label, not to its trigger', async () => {
    await openMenu();

    const classes = document.body.querySelector('[data-slot="dropdown-menu-content"]')?.className;

    expect(classes).toContain('w-max');
    expect(classes).not.toContain('--reka-dropdown-menu-trigger-width');
  });

  it('keeps every item label on one line', async () => {
    await openMenu();

    const item = document.body.querySelector('[data-slot="dropdown-menu-item"]');

    expect(item?.className).toContain('whitespace-nowrap');
  });
});
