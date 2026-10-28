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

import { i18n } from '@/i18n';

import FieldInfo from '../FieldInfo.vue';

describe('the field info button', () => {
  it('is a 36 px icon button with a 20 px icon, so a finger or a pointer can hit it', () => {
    const wrapper = mount(FieldInfo, {
      attachTo: document.body,
      global: { plugins: [i18n] },
      slots: { default: 'Help text' },
    });
    const button = wrapper.get('[data-slot="field-info"]');
    const icon = wrapper.get('svg');

    expect(button.classes()).toContain('size-9!');
    expect(button.classes()).not.toContain('size-4!');
    expect(icon.classes()).toContain('size-5');
    expect(icon.classes()).not.toContain('size-4');
  });
});
