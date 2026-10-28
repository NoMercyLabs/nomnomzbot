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

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import { i18n } from '@/i18n';

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

function panel(state: PanelState, extra: Record<string, unknown> = {}): VueWrapper {
  return mount(PanelCard, {
    props: {
      title: 'Chat',
      state,
      ...extra,
    },
    slots: { default: '<p>the rows</p>' },
    global: { plugins: [i18n] },
  });
}

// Stoney 2026-10-14: the description wrapped word by word beside the header tabs ("stop making me nitpick").
describe('the panel card header', () => {
  it('keeps the actions on the title row and gives the description the full width below', () => {
    const wrapper = mount(PanelCard, {
      props: {
        title: 'This session',
        description: '0 test runs since you opened this page',
      },
      slots: { actions: '<div data-tabs>All Failed Tests</div>' },
      global: { plugins: [i18n] },
    });
    const header = wrapper.find('header');
    const actions = wrapper.find('[data-slot="panel-actions"]');
    const description = header.find('p');

    // The title keeps its whole width; when the actions do not fit beside it they move to their own row
    // (owner 2026-10-14: "this session is wrapped when we have more than enough room").
    expect(header.classes()).toEqual(expect.arrayContaining(['flex', 'flex-wrap']));
    expect(header.find('h2').classes()).toEqual(expect.arrayContaining(['flex-[1_0_auto]', 'max-w-full']));
    expect(actions.find('[data-tabs]').exists()).toBe(true);
    // The growing title pushes the actions right; wrapped, they start at the left edge with the title and the
    // description, never right-aligned under them (owner 2026-10-14: "you created a staircase again").
    expect(actions.classes()).not.toContain('ml-auto');
    expect(description.classes()).toContain('basis-full');
  });

  // Mobile ledger MU-01: the Add button and the Playback switch cannot fit one 390 px row, so the actions wrap.
  it('lets several actions wrap onto a second line instead of overflowing', () => {
    const wrapper = mount(PanelCard, {
      props: { title: 'Now playing' },
      slots: { actions: '<button>Add button</button><label>Playback</label>' },
      global: { plugins: [i18n] },
    });

    expect(wrapper.find('[data-slot="panel-actions"]').classes()).toContain('flex-wrap');
  });
});

describe('the panel card states', () => {
  it('shows its rows only when ready', () => {
    expect(panel('ready').text()).toContain('the rows');
    expect(panel('empty').text()).not.toContain('the rows');
    expect(panel('error').text()).not.toContain('the rows');
    expect(panel('loading').text()).not.toContain('the rows');
  });

  it('shows no skeleton before 150 ms and a skeleton after', async () => {
    const wrapper = panel('loading');

    vi.advanceTimersByTime(149);
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-slot="skeleton"]').exists()).toBe(false);

    vi.advanceTimersByTime(1);
    await wrapper.vm.$nextTick();
    expect(wrapper.find('[data-slot="skeleton"]').exists()).toBe(true);
  });

  it('never shows a skeleton when the data arrives inside 150 ms', async () => {
    const wrapper = panel('loading');

    vi.advanceTimersByTime(100);
    await wrapper.setProps({ state: 'ready' });
    vi.advanceTimersByTime(500);
    await wrapper.vm.$nextTick();

    expect(wrapper.find('[data-slot="skeleton"]').exists()).toBe(false);
    expect(wrapper.text()).toContain('the rows');
  });

  it('shows the empty text it is given, else the shared words', () => {
    expect(panel('empty', { emptyText: 'No commands yet.' }).text()).toContain('No commands yet.');
    expect(panel('empty').text()).toContain('Nothing here yet.');
  });

  it('shows the error with a retry that emits retry', async () => {
    const wrapper = panel('error');

    expect(wrapper.find('[role="alert"]').text()).toContain('This panel could not load.');
    await wrapper.find('button').trigger('click');

    expect(wrapper.emitted('retry')).toHaveLength(1);
  });

  it('keeps the same head and body frame in every state', () => {
    const states: PanelState[] = ['ready', 'loading', 'empty', 'error'];

    states.forEach((state) => {
      const wrapper = panel(state);

      expect(wrapper.find('h2').text()).toBe('Chat');
      expect(wrapper.find('.flex-1.overflow-y-auto').exists()).toBe(true);
    });
  });
});
