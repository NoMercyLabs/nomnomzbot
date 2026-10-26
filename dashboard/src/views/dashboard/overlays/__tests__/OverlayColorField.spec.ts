// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';

import OverlaySettingField from '../components/OverlaySettingField.vue';
import { cleanColor } from '../components/settingField';

const text = (value: string): { key: string; text: string } => ({ key: `t.${value}`, text: value });

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

function mountColor(value: string): VueWrapper {
  mounted = mount(OverlaySettingField, {
    props: {
      field: {
        key: 'baseColor',
        label: text('Base color'),
        type: 'color',
        group: text('Look'),
        default: '#A00030',
      },
      value,
      changed: true,
    },
    global: { plugins: [i18n] },
  });

  return mounted;
}

describe('cleanColor', () => {
  it('keeps one # and upper-cases a full hex color', () => {
    expect(cleanColor('##0072d4')).toBe('#0072D4');
    expect(cleanColor('0072d4')).toBe('#0072D4');
    expect(cleanColor(' #0072d4 ')).toBe('#0072D4');
  });

  it('leaves a half-typed color alone so typing is not fought', () => {
    expect(cleanColor('#00')).toBe('#00');
    expect(cleanColor('')).toBe('');
  });
});

describe('an overlay color field', () => {
  it('saves one clean color when a second # is typed in front', async () => {
    const field = mountColor('#0072d4');

    await field.get('[data-setting="baseColor"] input:not([type=color])').setValue('##0072d4');

    expect(field.emitted('update')?.at(-1)).toEqual(['#0072D4']);
  });

  it('offers the color picker, which saves the picked color', async () => {
    const field = mountColor('#0072d4');
    const picker = field.get('[data-setting="baseColor"] input[type=color]');

    await picker.setValue('#112233');

    expect(field.emitted('update')?.at(-1)).toEqual(['#112233']);
  });

  it('shows the default color in the swatch while a broken value is stored', () => {
    const field = mountColor('##0072d4');

    expect(field.get('[data-color-chip]').attributes('style')).toContain('background-color: rgb(160, 0, 48)');
  });
});
