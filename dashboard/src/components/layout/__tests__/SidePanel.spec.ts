// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { nextTick } from 'vue';

import { setWidth } from '@/__tests__/viewport';
import SidePanel from '@/components/layout/SidePanel.vue';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

async function panelAt(width: number, open = true): Promise<VueWrapper> {
  setWidth(width);

  mounted = mount(SidePanel, {
    attachTo: document.body,
    attrs: { 'data-display-panel': '' },
    props: {
      label: 'Display',
      open,
      'onUpdate:open': (value: boolean) =>
        mounted!.setProps({ open: value }),
    },
    slots: { default: '<p data-content>options</p>' },
  });

  await flushPromises();

  return mounted;
}

// Stoney 2026-10-14, on his phone: "Make sure you overlay what no longer fits with a drawer from left or right".
describe('the side panel', () => {
  it('sits beside the content in the row where the content keeps its room', async () => {
    const wrapper = await panelAt(1440);
    const panel = wrapper.find('aside[data-display-panel]');

    expect(panel.exists()).toBe(true);
    expect(panel.find('[data-content]').exists()).toBe(true);
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('slides over the page as a drawer where the content would be squeezed', async () => {
    const wrapper = await panelAt(412);
    const drawer = document.body.querySelector<HTMLElement>('[role="dialog"][data-display-panel]');

    expect(wrapper.find('aside').exists()).toBe(false);
    expect(drawer).not.toBeNull();
    expect(drawer!.dataset.side).toBe('right');
    expect(drawer!.querySelector('[data-content]')).not.toBeNull();
    expect(drawer!.textContent).toContain('Display');
  });

  it('opens the drawer without putting the focus on one of its controls', async () => {
    setWidth(412);

    mounted = mount(SidePanel, {
      attachTo: document.body,
      props: { label: 'Display', open: false },
      slots: { default: '<button data-reset>Reset</button>' },
    });

    await mounted.setProps({ open: true });
    await flushPromises();

    expect(document.body.querySelector('[data-reset]')).not.toBeNull();
    expect(document.activeElement).not.toBe(document.body.querySelector('[data-reset]'));
  });

  it('closes the drawer on Escape and hands the closed state back to the page', async () => {
    const wrapper = await panelAt(412);
    const drawer = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    drawer.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await flushPromises();
    await nextTick();

    expect(wrapper.emitted('update:open')).toEqual([[false]]);
  });

  it('shows nothing while closed, at any width', async () => {
    await panelAt(1440, false);
    expect(document.body.querySelector('[data-display-panel]')).toBeNull();

    mounted!.unmount();
    await panelAt(412, false);
    expect(document.body.querySelector('[data-display-panel]')).toBeNull();
  });
});
