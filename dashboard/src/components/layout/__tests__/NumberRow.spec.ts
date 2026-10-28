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
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';

import NumberRow from '../NumberRow.vue';

function build(props: Record<string, unknown>) {
  return mount(NumberRow, {
    props: {
      title: 'Ban penalty',
      modelValue: 3,
      ...props,
    },
    global: { plugins: [i18n] },
  });
}

describe('NumberRow', () => {
  it('sends a number, not text, when the person types', async () => {
    const wrapper = build({});

    await wrapper.get('input').setValue('4.5');

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([4.5]);
  });

  it('shows the default beside the title and offers a reset only while the value differs', async () => {
    const wrapper = build({
      hint: 'Default 9',
      defaultValue: 9,
    });

    expect(wrapper.get('[data-row-hint]').text()).toBe('Default 9');
    expect(wrapper.find('[data-action="reset-field"]').exists()).toBe(true);

    await wrapper.setProps({ modelValue: 9 });

    expect(wrapper.find('[data-action="reset-field"]').exists()).toBe(false);
  });

  it('puts the reset value into the model', async () => {
    const wrapper = build({ defaultValue: 9 });

    await wrapper.get('[data-action="reset-field"]').trigger('click');

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([9]);
  });

  it('shows a refusal under the row and marks the input invalid', () => {
    const wrapper = build({ error: 'Too high' });

    expect(wrapper.get('[data-row-error]').text()).toBe('Too high');
    expect(wrapper.get('input').attributes('aria-invalid')).toBe('true');
  });
});

describe('number inputs on the Trust policy page', () => {
  it('come from NumberRow, never a hand-written type="number" input', () => {
    const folder = 'src/views/dashboard/settings/trust-policy';
    const sources = [`${folder}/TrustPolicyView.vue`, `${folder}/components/TrustGroupPanel.vue`].map(
      (path) => readFileSync(path, 'utf8'),
    );

    expect(sources.filter((source) => source.includes('type="number"'))).toEqual([]);
    expect(sources.some((source) => source.includes('<NumberRow'))).toBe(true);
  });
});
