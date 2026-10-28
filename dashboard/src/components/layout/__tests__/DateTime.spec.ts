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
import { afterEach, describe, expect, it } from 'vitest';
import { nextTick } from 'vue';

import DateTime from '@/components/layout/DateTime.vue';
import { i18n } from '@/i18n';

process.env.TZ = 'UTC';

const MOMENT: Date = new Date(Date.UTC(2026, 9, 18, 14, 5, 3));

afterEach(() => {
  i18n.global.locale.value = 'en';
});

function render(props: InstanceType<typeof DateTime>['$props']): VueWrapper {
  return mount(DateTime, { props, global: { plugins: [i18n] } });
}

describe('DateTime', () => {
  it('renders a time element with the machine value, the text and the exact moment on hover', () => {
    const wrapper = render({ value: MOMENT, variant: 'time' });
    const time = wrapper.get('time');

    expect(time.text()).toBe('2:05 PM');
    expect(time.attributes('datetime')).toBe('2026-10-18T14:05:03.000Z');
    expect(time.attributes('title')).toBe('October 18, 2026 at 2:05:03 PM');
  });

  it('re-renders in the new language when the locale switches', async () => {
    const wrapper = render({ value: MOMENT, variant: 'time' });

    i18n.global.locale.value = 'nl';
    await nextTick();

    expect(wrapper.get('time').text()).toBe('14:05');
    expect(wrapper.get('time').attributes('title')).toBe('18 oktober 2026 om 14:05:03');
  });

  it('keeps the exact moment on a relative label', () => {
    const wrapper = render({ value: new Date(Date.now() - 180_000), variant: 'relative' });

    expect(wrapper.get('time').text()).toBe('3 minutes ago');
    expect(wrapper.get('time').attributes('title')).toContain('2026');
  });

  it('writes a length with an ISO duration and no hover title', () => {
    const wrapper = render({ value: 3723, variant: 'durationClock' });

    expect(wrapper.get('time').text()).toBe('1:02:03');
    expect(wrapper.get('time').attributes('datetime')).toBe('PT3723S');
    expect(wrapper.get('time').attributes('title')).toBeUndefined();
  });

  it('renders a dash for null and for an invalid value, without a time element', () => {
    for (const value of [null, undefined, 'garbage']) {
      const wrapper = render({ value, variant: 'date' });

      expect(wrapper.text()).toBe('–');
      expect(wrapper.find('time').exists()).toBe(false);
    }
  });
});
