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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, nextTick } from 'vue';

import { i18n } from '@/i18n';
import { formatTime, TIME_VARIANTS, type TimeValue } from '@/lib/time';

import { useFormattedTime, useTime } from '../useTime';

process.env.TZ = 'UTC';

const START: number = Date.UTC(2026, 9, 18, 14, 5, 3);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(START);
});

afterEach(() => {
  vi.useRealTimers();
  i18n.global.locale.value = 'en';
});

function inComponent<T>(setup: () => T): T {
  let result: T | undefined;
  const host = defineComponent({
    setup() {
      result = setup();

      return () => null;
    },
  });

  mount(host, { global: { plugins: [i18n] } });

  return result as T;
}

function sampleFor(variant: string): TimeValue {
  return variant.startsWith('duration')
    ? 3723
    : START - 180_000;
}

describe('useTime', () => {
  it('has one named method per variant that matches formatTime in en and nl', () => {
    const time = inComponent(() => useTime());

    for (const locale of ['en', 'nl'] as const) {
      i18n.global.locale.value = locale;

      for (const variant of TIME_VARIANTS) {
        const sample = sampleFor(variant);
        const expected = formatTime(sample, variant, locale);

        expect(time[variant](sample)).toBe(expected);
      }
    }
  });

  it('writes the same moment differently once the language switches', () => {
    const time = inComponent(() => useTime());

    expect(time.time(START)).toBe('2:05 PM');

    i18n.global.locale.value = 'nl';

    expect(time.time(START)).toBe('14:05');
  });
});

describe('useFormattedTime', () => {
  it('moves a live relative text on as time passes', async () => {
    const text = inComponent(() => useFormattedTime(START - 10_000, 'relative', { live: true }));

    expect(text.value).toBe('10 seconds ago');

    vi.advanceTimersByTime(20_000);
    await nextTick();

    expect(text.value).toBe('30 seconds ago');

    vi.advanceTimersByTime(120_000);
    await nextTick();

    expect(text.value).toBe('2 minutes ago');
  });

  it('keeps a text that is not live', async () => {
    const text = inComponent(() => useFormattedTime(START - 10_000, 'relative'));

    vi.advanceTimersByTime(120_000);
    await nextTick();

    expect(text.value).toBe('10 seconds ago');
  });

  it('follows the language', () => {
    const text = inComponent(() => useFormattedTime(START, 'time'));

    i18n.global.locale.value = 'nl';

    expect(text.value).toBe('14:05');
  });
});
