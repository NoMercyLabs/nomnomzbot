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
import { defineComponent, h, ref } from 'vue';

import EditorFooter from '@/components/layout/EditorFooter.vue';
import { i18n } from '@/i18n';

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

const full = {
  deleteLabel: 'Delete command',
  discardLabel: 'Discard',
  saveLabel: 'Save',
  unsaved: '2 unsaved changes',
  confirmTitle: 'Delete !so?',
  confirmDescription: 'This cannot be undone.',
  confirmAction: 'Delete !so',
};

function footer(props: Record<string, unknown>, slots: Record<string, string> = {}): VueWrapper {
  mounted = mount(EditorFooter, {
    props,
    slots,
    attachTo: document.body,
    global: { plugins: [i18n] },
  });

  return mounted;
}

function order(wrapper: VueWrapper): string[] {
  const row = wrapper.get('[data-slot="editor-footer"]').element;

  return Array.from(row.children).map((child) => child.textContent?.trim() ?? '');
}

describe('the editor footer', () => {
  it('lays out Delete, the extra actions, the unsaved text, Discard and Save, in that order', () => {
    const wrapper = footer(full, { extra: '<button>Duplicate</button>' });

    expect(order(wrapper)).toEqual([
      'Delete command',
      'Duplicate',
      '2 unsaved changes',
      'Discard',
      'Save',
    ]);
  });

  it('makes Save the one primary action and Delete quiet', () => {
    const wrapper = footer(full);

    expect(wrapper.get('[data-editor-save]').attributes('data-variant')).toBe('default');
    expect(wrapper.get('[data-editor-discard]').attributes('data-variant')).toBe('outline');
    expect(wrapper.get('[data-editor-delete]').attributes('data-variant')).toBe('ghost');
  });

  it('emits discard and save, and keeps Save off until the form is saveable', async () => {
    const wrapper = footer({ ...full, canSave: false });

    expect(wrapper.get('[data-editor-save]').attributes('disabled')).toBeDefined();

    await wrapper.setProps({ canSave: true });
    await wrapper.get('[data-editor-discard]').trigger('click');
    await wrapper.get('[data-editor-save]').trigger('click');

    expect(wrapper.emitted('discard')).toHaveLength(1);
    expect(wrapper.emitted('save')).toHaveLength(1);
  });

  it('only asks on Delete; the delete event follows the confirm and nothing before it', async () => {
    const Host = defineComponent({
      emits: ['gone'],
      setup: (_props, { emit }) => {
        const open = ref(false);

        return () => h(EditorFooter, {
          ...full,
          confirmOpen: open.value,
          'onUpdate:confirmOpen': (value: boolean) => {
            open.value = value;
          },
          onAskDelete: () => {
            open.value = true;
          },
          onConfirmDelete: () => emit('gone'),
        });
      },
    });

    mounted = mount(Host, { attachTo: document.body, global: { plugins: [i18n] } });

    await mounted.get('[data-editor-delete]').trigger('click');
    await flushPromises();

    expect(mounted.emitted('gone')).toBeUndefined();
    expect(document.body.querySelector('[role="alertdialog"]')?.textContent).toContain('Delete !so?');

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await flushPromises();

    expect(mounted.emitted('gone')).toHaveLength(1);
  });

  it('shows Reset in the place of Delete for a fixed-set item, with no Delete and no confirm', async () => {
    const wrapper = footer({
      resetLabel: 'Use the default',
      saveLabel: 'Save',
      discardLabel: 'Discard',
    });

    expect(wrapper.find('[data-editor-delete]').exists()).toBe(false);
    expect(order(wrapper)[0]).toBe('Use the default');

    await wrapper.get('[data-editor-reset]').trigger('click');

    expect(wrapper.emitted('reset')).toHaveLength(1);
    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('drops Discard and Save for an editor that saves each change at once', () => {
    const wrapper = footer({ deleteLabel: 'Delete script' });

    expect(wrapper.find('[data-editor-save]').exists()).toBe(false);
    expect(wrapper.find('[data-editor-discard]').exists()).toBe(false);
    expect(wrapper.find('[data-editor-delete]').exists()).toBe(true);
  });
});
