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
import { railWanted } from '@/composables/useFrameRail';

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

  // Stoney 2026-10-14: "the chat settings, it needs to be seen what it does, so maybe we can collapse the sidebar
  // to give us more options". A see-through panel folds the sidebar to its rail to stay in the row, and where
  // even that has no room it opens as a drawer without the blur.
  describe('a see-through panel', () => {
    async function seeThroughAt(width: number, open = true): Promise<VueWrapper> {
      setWidth(width);

      mounted = mount(SidePanel, {
        attachTo: document.body,
        props: {
          label: 'Display',
          open,
          seeThrough: true,
        },
        slots: { default: '<p data-content>options</p>' },
      });

      await flushPromises();

      return mounted;
    }

    it('folds the sidebar to its rail to stay beside the content', async () => {
      const wrapper = await seeThroughAt(1160);

      expect(wrapper.find('aside[data-slot="side-panel"]').exists()).toBe(true);
      expect(railWanted.value).toBe(true);

      await wrapper.setProps({ open: false });
      expect(railWanted.value).toBe(false);
    });

    it('leaves the sidebar alone where the panel fits beside it', async () => {
      await seeThroughAt(1440);

      expect(railWanted.value).toBe(false);
    });

    it('opens as a drawer with no blur where even the rail leaves no room', async () => {
      await seeThroughAt(900);
      const drawer = document.body.querySelector<HTMLElement>('[role="dialog"][data-slot="side-panel"]');

      expect(drawer?.dataset.seeThrough).toBe('true');
      expect(railWanted.value).toBe(false);
    });
  });

  it('shows nothing while closed, at any width', async () => {
    await panelAt(1440, false);
    expect(document.body.querySelector('[data-display-panel]')).toBeNull();

    mounted!.unmount();
    await panelAt(412, false);
    expect(document.body.querySelector('[data-display-panel]')).toBeNull();
  });
});
