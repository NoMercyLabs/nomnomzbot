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

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { timecodeSeconds, useLiveTimer } from '../useLiveTimer';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-10-18T12:00:00Z'));
});

afterEach(() => {
  vi.useRealTimers();
});

describe('timecodeSeconds', () => {
  it('reads an OBS timecode as whole and fractional seconds', () => {
    expect(timecodeSeconds('01:02:03.500')).toBe(3723.5);
    expect(timecodeSeconds('00:00:09')).toBe(9);
  });

  it('reads an empty or broken timecode as zero', () => {
    expect(timecodeSeconds(null)).toBe(0);
    expect(timecodeSeconds('')).toBe(0);
    expect(timecodeSeconds('soon')).toBe(0);
  });
});

describe('useLiveTimer', () => {
  it('counts up every second from the last known timecode', async () => {
    const timer = useLiveTimer(ref('00:00:58.000'), ref(true));

    expect(timer.value).toBe('0:58');

    await vi.advanceTimersByTimeAsync(3000);

    expect(timer.value).toBe('1:01');
  });

  it('starts over from a new timecode', async () => {
    const timecode = ref('00:00:10.000');
    const timer = useLiveTimer(timecode, ref(true));

    await vi.advanceTimersByTimeAsync(5000);

    expect(timer.value).toBe('0:15');

    timecode.value = '00:01:00.000';

    expect(timer.value).toBe('1:00');

    await vi.advanceTimersByTimeAsync(2000);

    expect(timer.value).toBe('1:02');
  });

  it('is empty while the output is off and begins at zero when it starts', async () => {
    const running = ref(false);
    const timer = useLiveTimer(ref(null), running);

    expect(timer.value).toBe('');

    running.value = true;
    await vi.advanceTimersByTimeAsync(4000);

    expect(timer.value).toBe('0:04');
  });

  it('holds while paused and counts on from there after the resume', async () => {
    const ticking = ref(true);
    const timer = useLiveTimer(ref('00:00:10.000'), ref(true), ticking);

    await vi.advanceTimersByTimeAsync(2000);

    ticking.value = false;
    await vi.advanceTimersByTimeAsync(30000);

    expect(timer.value).toBe('0:12');

    ticking.value = true;
    await vi.advanceTimersByTimeAsync(3000);

    expect(timer.value).toBe('0:15');
  });
});
