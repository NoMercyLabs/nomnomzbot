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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';

let mounted: VueWrapper | undefined;

beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  mounted?.unmount();
  document.body.innerHTML = '';
});

// The page header slot and the frame's sticky footer, as the app frame lays them out.
const Frame = defineComponent({
  props: { reason: { type: String, default: undefined } },
  emits: ['create'],
  setup: (props, { emit }) => () => {
    const button = h(PageCreateButton, {
      label: 'New command',
      disabledReason: props.reason,
      onCreate: () => emit('create'),
    });

    return h('div', [
      h('header', { 'data-header': '' }, button),
      h('div', { 'data-slot': 'frame-action-footer' }),
    ]);
  },
});

async function mountFrame(width: number, reason?: string): Promise<VueWrapper> {
  // This spec brings its own frame; the setup file's stand-in would take the teleported content first.
  document.body.innerHTML = '';
  setWidth(width);

  mounted = mount(Frame, { props: { reason }, attachTo: document.body });

  await flushPromises();

  return mounted;
}

describe('the page create button', () => {
  it('is the default variant at size sm with a Plus icon, and emits create on a click', async () => {
    const wrapper = await mountFrame(laptopWidth);
    const button = wrapper.get('[data-header] button');

    expect(button.attributes('data-variant')).toBe('default');
    expect(button.attributes('data-size')).toBe('sm');
    expect(button.text()).toBe('New command');
    expect(button.find('svg').exists()).toBe(true);
    expect(button.attributes('disabled')).toBeUndefined();

    await button.trigger('click');

    expect(wrapper.emitted('create')).toHaveLength(1);
  });

  it('stays visible but disabled with the reason as a tooltip when creating is off', async () => {
    const wrapper = await mountFrame(laptopWidth, 'You can view this, but your role cannot change it.');
    const button = wrapper.get('[data-header] button');

    expect(button.attributes('disabled')).toBeDefined();

    await button.trigger('click');

    expect(wrapper.emitted('create')).toBeUndefined();

    await wrapper.get('[data-slot="page-create"]').trigger('focus');
    await flushPromises();

    expect(document.body.querySelector('[data-slot="tooltip-content"]')?.textContent).toContain(
      'You can view this, but your role cannot change it.',
    );
  });

  it('moves full width into the frame footer below the tablet width, still the same button', async () => {
    const wrapper = await mountFrame(phoneWidth);

    expect(wrapper.find('[data-header] button').exists()).toBe(false);

    const button = wrapper.get('[data-slot=frame-action-footer] button');

    expect(button.text()).toBe('New command');
    expect(button.classes()).toContain('w-full');
    expect(button.attributes('data-variant')).toBe('default');
  });
});
