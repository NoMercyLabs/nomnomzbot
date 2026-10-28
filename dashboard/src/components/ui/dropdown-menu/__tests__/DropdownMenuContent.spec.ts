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
