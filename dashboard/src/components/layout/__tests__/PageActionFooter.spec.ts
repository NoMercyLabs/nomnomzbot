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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';

// The frame's footer slot above the bottom bar, and a page that puts its Save in the page footer.
const save = () => h('button', 'Save');
const page = () => h('section', { 'data-page': '' }, h(PageActionFooter, save));
const Frame = defineComponent({
  render: () => {
    const main = h('main', page());
    const slot = h('div', { 'data-slot': 'frame-action-footer' });

    return h('div', [main, slot]);
  },
});

async function mountFrame(width: number): Promise<VueWrapper> {
  // This spec brings its own frame; the setup file's stand-in would take the teleported content first.
  document.body.innerHTML = '';
  setWidth(width);

  const wrapper = mount(Frame, { attachTo: document.body });

  await flushPromises();

  return wrapper;
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the page action footer', () => {
  it('moves the page actions into the frame footer slot on a phone', async () => {
    const wrapper = await mountFrame(phoneWidth);

    expect(wrapper.find('[data-slot=frame-action-footer]').text()).toBe('Save');
    expect(wrapper.find('[data-page]').text()).toBe('');
  });

  it('keeps the page actions where the page put them from the tablet width up', async () => {
    const wrapper = await mountFrame(laptopWidth);

    expect(wrapper.find('[data-page]').text()).toBe('Save');
    expect(wrapper.find('[data-slot=frame-action-footer]').text()).toBe('');
  });
});
