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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import Countdown from '@/components/layout/Countdown.vue';
import { formatCountdown } from '@/components/layout/formatCountdown';
import { i18n } from '@/i18n';

const START: number = Date.UTC(2026, 9, 13, 12, 0, 0);

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(START);
});

afterEach(() => {
  vi.useRealTimers();
});

function countdown(secondsAhead: number, extra: Record<string, unknown> = {}): VueWrapper {
  return mount(Countdown, {
    props: {
      target: START + secondsAhead * 1000,
      label: 'Next ad',
      ...extra,
    },
    global: { plugins: [i18n] },
  });
}

function shown(wrapper: VueWrapper): string {
  return wrapper.find('[data-slot="countdown-time"]').text();
}

describe('formatCountdown', () => {
  it('shows m:ss under an hour and h:mm:ss from an hour on', () => {
    expect(formatCountdown(0)).toBe('0:00');
    expect(formatCountdown(59)).toBe('0:59');
    expect(formatCountdown(303)).toBe('5:03');
    expect(formatCountdown(3599)).toBe('59:59');
    expect(formatCountdown(3600)).toBe('1:00:00');
    expect(formatCountdown(3725)).toBe('1:02:05');
  });
});

describe('the countdown', () => {
  it('counts down one second at a time', async () => {
    const wrapper = countdown(303);

    expect(shown(wrapper)).toBe('5:03');

    vi.advanceTimersByTime(1000);
    await wrapper.vm.$nextTick();
    expect(shown(wrapper)).toBe('5:02');

    vi.advanceTimersByTime(62_000);
    await wrapper.vm.$nextTick();
    expect(shown(wrapper)).toBe('4:00');
  });

  it('emits zero exactly once when the time runs out and stays at 0:00', async () => {
    const wrapper = countdown(3);

    vi.advanceTimersByTime(2000);
    await wrapper.vm.$nextTick();
    expect(wrapper.emitted('zero')).toBeUndefined();

    vi.advanceTimersByTime(1000);
    await wrapper.vm.$nextTick();
    expect(wrapper.emitted('zero')).toHaveLength(1);
    expect(shown(wrapper)).toBe('0:00');

    vi.advanceTimersByTime(10_000);
    await wrapper.vm.$nextTick();
    expect(wrapper.emitted('zero')).toHaveLength(1);
    expect(shown(wrapper)).toBe('0:00');
  });

  it('shows 0:00 and emits zero at once for a target in the past', async () => {
    const wrapper = countdown(-90);

    await wrapper.vm.$nextTick();

    expect(shown(wrapper)).toBe('0:00');
    expect(wrapper.emitted('zero')).toHaveLength(1);
  });

  it('restarts when the target changes, and emits zero again for the new target', async () => {
    const wrapper = countdown(2);

    vi.advanceTimersByTime(2000);
    await wrapper.vm.$nextTick();
    expect(wrapper.emitted('zero')).toHaveLength(1);

    await wrapper.setProps({ target: Date.now() + 120_000 });
    expect(shown(wrapper)).toBe('2:00');
    expect(wrapper.emitted('zero')).toHaveLength(1);

    vi.advanceTimersByTime(120_000);
    await wrapper.vm.$nextTick();
    expect(shown(wrapper)).toBe('0:00');
    expect(wrapper.emitted('zero')).toHaveLength(2);
  });

  it('keeps the seconds out of the screen reader text, which changes only once a minute', async () => {
    const wrapper = countdown(190);
    const spoken = (): string => wrapper.find('.sr-only').text();
    const first = spoken();

    expect(first).toContain('Next ad');
    expect(first).toContain('4');
    expect(wrapper.find('[data-slot="countdown-time"]').attributes('aria-hidden')).toBe('true');
    expect(wrapper.attributes('aria-live')).toBeUndefined();

    vi.advanceTimersByTime(5000);
    await wrapper.vm.$nextTick();
    expect(spoken()).toBe(first);

    vi.advanceTimersByTime(60_000);
    await wrapper.vm.$nextTick();
    expect(spoken()).not.toBe(first);
  });

  it('marks the last minute and uses the shared words at zero', async () => {
    const wrapper = countdown(61);

    expect(wrapper.attributes('data-soon')).toBeUndefined();

    vi.advanceTimersByTime(2000);
    await wrapper.vm.$nextTick();
    expect(wrapper.attributes('data-soon')).toBe('true');

    vi.advanceTimersByTime(60_000);
    await wrapper.vm.$nextTick();
    expect(wrapper.find('.sr-only').text()).toBe('Next ad: now');
  });
});
