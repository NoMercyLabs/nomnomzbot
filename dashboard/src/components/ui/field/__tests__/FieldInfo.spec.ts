// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
