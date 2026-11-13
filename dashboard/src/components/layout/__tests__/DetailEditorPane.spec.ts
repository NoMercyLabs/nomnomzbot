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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';

const props = {
  idleTitle: 'Pick a timer',
  idleText: 'Choose a timer to edit it.',
  missingText: 'That timer is gone.',
  backLabel: 'Back to timers',
};
const editor = '<div data-editor>Editor</div>';
const mocks = { $t: (key: string): string => key };

describe('the detail editor pane', () => {
  it('shows the idle title and text, and no editor or Back link, when nothing is open', () => {
    const wrapper = mount(DetailEditorPane, {
      props: {
        ...props,
        open: false,
      },
      slots: { default: editor },
      global: { mocks },
    });
    const empty = wrapper.find('[data-slot="panel-empty"]');

    expect(wrapper.text()).toContain('Pick a timer');
    expect(empty.attributes('data-empty-kind')).toBe('none');
    expect(empty.text()).toContain('Choose a timer to edit it.');
    expect(wrapper.find('[data-editor]').exists()).toBe(false);
    expect(wrapper.find('[data-detail-back]').exists()).toBe(false);
  });

  it('shows the missing text, with the Back link, when the open row is gone', () => {
    const wrapper = mount(DetailEditorPane, {
      props: {
        ...props,
        open: true,
        missing: true,
      },
      slots: { default: editor },
      global: { mocks },
    });
    const empty = wrapper.find('[data-slot="panel-empty"]');

    expect(empty.attributes('data-empty-kind')).toBe('noMatch');
    expect(empty.text()).toContain('That timer is gone.');
    expect(wrapper.find('[data-editor]').exists()).toBe(false);
    expect(wrapper.find('[data-detail-back]').exists()).toBe(true);
  });

  it('shows the editor and a phone-only Back link that emits back', async () => {
    const wrapper = mount(DetailEditorPane, {
      props: {
        ...props,
        open: true,
      },
      slots: { default: editor },
      global: { mocks },
    });
    const back = wrapper.find('[data-detail-back]');

    expect(wrapper.find('[data-editor]').exists()).toBe(true);
    expect(wrapper.find('[data-slot="panel-empty"]').exists()).toBe(false);
    expect(back.text()).toContain('Back to timers');
    expect(back.classes()).toContain('md:hidden');

    await back.trigger('click');

    expect(wrapper.emitted('back')).toHaveLength(1);
  });
});
