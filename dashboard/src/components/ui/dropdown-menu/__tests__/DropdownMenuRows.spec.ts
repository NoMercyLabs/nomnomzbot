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

import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSub,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '..';

// Stoney 2026-10-21: "what works on desktop does not work for mobile". A finger needs 36 px; the desktop rows stay 32 px.
describe('a dropdown menu row on a phone', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('is at least 36 px tall for every kind of row, and unchanged from the tablet width up', async () => {
    const wrapper = mount(
      {
        render: () =>
          h(DropdownMenu, null, () => [
            h(DropdownMenuTrigger, { 'data-trigger': '' }, () => 'Open'),
            h(DropdownMenuContent, null, () => [
              h(DropdownMenuItem, null, () => 'Plain'),
              h(DropdownMenuCheckboxItem, { modelValue: true }, () => 'Checked'),
              h(DropdownMenuRadioGroup, { modelValue: 'a' }, () =>
                h(DropdownMenuRadioItem, { value: 'a' }, () => 'Radio')),
              h(DropdownMenuSub, null, () =>
                h(DropdownMenuSubTrigger, null, () => 'Submenu')),
            ]),
          ]),
      },
      { attachTo: document.body },
    );

    await wrapper.get('[data-trigger]').trigger('keydown', { key: 'Enter' });
    await flushPromises();

    const rows = document.body.querySelectorAll<HTMLElement>('[role^="menuitem"]');

    expect(rows).toHaveLength(4);

    rows.forEach((row) => {
      expect(row.className).toContain('max-md:min-h-9');
      expect(row.className).not.toMatch(/(^|\s)min-h-9/);
    });
  });
});
