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

import { parseClock } from '../formatCountdown';
import DurationInput from '../DurationInput.vue';

// Stoney 2026-10-18: "Longest song, in seconds" needs to be minutes, don't make the user do math.
function field(modelValue: number, extra: Record<string, unknown> = {}) {
  return mount(DurationInput, { props: {
    modelValue,
    min: 10,
    max: 3600,
    ...extra,
  } });
}

describe('DurationInput', () => {
  it('shows seconds from the API as minutes and seconds', () => {
    expect(field(270).get('input').element.value).toBe('4:30');
    expect(field(3600).get('input').element.value).toBe('60:00');
    expect(field(45).get('input').element.value).toBe('0:45');
  });

  it('sends seconds when the user types minutes and seconds', async () => {
    const wrapper = field(60);

    await wrapper.get('input').setValue('4:30');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([270]);
  });

  it('reads bare digits as minutes', async () => {
    const wrapper = field(60);

    await wrapper.get('input').setValue('6');

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([360]);
  });

  it('sends NaN for text that is not a duration and flags the field', async () => {
    const wrapper = field(60);

    await wrapper.get('input').setValue('4:99');

    const sent = wrapper.emitted('update:modelValue')?.at(-1)?.[0];

    expect(Number.isNaN(sent)).toBe(true);
    expect(wrapper.get('input').attributes('aria-invalid')).toBe('true');
  });

  it('flags a value outside min and max', async () => {
    const wrapper = field(60);

    await wrapper.get('input').setValue('0:05');

    expect(wrapper.get('input').attributes('aria-invalid')).toBe('true');
  });

  it('follows a new value from the parent', async () => {
    const wrapper = field(60);

    await wrapper.setProps({ modelValue: 270 });

    expect(wrapper.get('input').element.value).toBe('4:30');
  });
});

describe('parseClock', () => {
  it('parses m:ss, bare minutes and rejects the rest', () => {
    expect(parseClock('4:30')).toBe(270);
    expect(parseClock(' 12:05 ')).toBe(725);
    expect(parseClock('3')).toBe(180);
    expect(Number.isNaN(parseClock('4:5'))).toBe(true);
    expect(Number.isNaN(parseClock('abc'))).toBe(true);
    expect(Number.isNaN(parseClock(''))).toBe(true);
  });
});
