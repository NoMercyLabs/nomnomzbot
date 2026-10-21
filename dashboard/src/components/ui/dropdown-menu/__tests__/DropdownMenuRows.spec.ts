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
