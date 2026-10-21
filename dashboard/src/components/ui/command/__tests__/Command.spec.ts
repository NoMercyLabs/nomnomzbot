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
import { describe, expect, it } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem } from '..';

// The list a server answers after the person typed: its row is added once the search is already set.
const Harness = defineComponent({
  setup(_, { expose }) {
    const rows = ref<string[]>([]);

    expose({ rows });

    return () =>
      h(Command, null, () => [
        h(CommandInput),
        h(CommandEmpty, null, () => 'No channel found.'),
        h(CommandGroup, { heading: 'Found' }, () =>
          rows.value.map((name) => h(CommandItem, { key: name, value: name }, () => name))),
      ]);
  },
});

describe('Command', () => {
  it('shows a row that arrives after the search and drops the empty line', async () => {
    const wrapper = mount(Harness, { attachTo: document.body });
    const input = wrapper.find('input');

    await input.setValue('tbdgamer');
    await flushPromises();
    expect(wrapper.text()).toContain('No channel found.');

    (wrapper.vm as unknown as { rows: string[] }).rows = ['tbdgamer'];
    await flushPromises();

    expect(wrapper.text()).toContain('tbdgamer');
    expect(wrapper.text()).not.toContain('No channel found.');
    wrapper.unmount();
  });
});
