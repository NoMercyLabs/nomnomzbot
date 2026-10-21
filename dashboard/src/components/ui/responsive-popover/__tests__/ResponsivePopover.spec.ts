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

import { ResponsivePopover, ResponsivePopoverContent, ResponsivePopoverTrigger } from '..';

// A window of the given width answers both min-width and max-width queries.
function setWindow(width: number): void {
  window.innerWidth = width;
  window.matchMedia = ((query: string): MediaQueryList => {
    const min = query.match(/min-width:\s*([\d.]+)px/);
    const max = query.match(/max-width:\s*([\d.]+)px/);
    const matches = (min
      ? width >= Number(min[1])
      : false) || (max
      ? width <= Number(max[1])
      : false);

    return {
      matches,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
      onchange: null,
    } as MediaQueryList;
  }) as typeof window.matchMedia;
}

async function openAt(width: number): Promise<void> {
  setWindow(width);

  const wrapper = mount(
    {
      render: () =>
        h(ResponsivePopover, null, () => [
          h(ResponsivePopoverTrigger, null, () => h('button', { 'data-trigger': '' }, 'Open')),
          h(ResponsivePopoverContent, { title: 'Channels', class: 'w-72' }, () => h('p', 'Body text')),
        ]),
    },
    { attachTo: document.body },
  );

  await wrapper.get('[data-trigger]').trigger('click');
  await flushPromises();
}

describe('ResponsivePopover', () => {
  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('opens a full-width bottom sheet on a phone, not a popover', async () => {
    await openAt(390);

    const sheet = document.body.querySelector('[data-slot="responsive-sheet"]');

    expect(sheet?.textContent).toContain('Body text');
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull();
    expect(document.body.querySelector('[data-slot="drawer-title"]')?.textContent).toBe('Channels');
  });

  it('keeps the popover, with its own width class, on a laptop', async () => {
    await openAt(1280);

    const popover = document.body.querySelector('[data-slot="popover-content"]');

    expect(popover?.textContent).toContain('Body text');
    expect(popover?.className).toContain('w-72');
    expect(document.body.querySelector('[data-slot="responsive-sheet"]')).toBeNull();
  });
});
