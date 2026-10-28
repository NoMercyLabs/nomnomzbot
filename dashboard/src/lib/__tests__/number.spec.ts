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

import { describe, expect, it } from 'vitest';

import { formatBytes } from '@/lib/number';
import { formatSpan } from '@/lib/time';

describe('formatBytes', () => {
  it('picks the biggest unit in which the size is at least one', () => {
    expect(formatBytes(3_394_022, 'en')).toBe('3.2 MB');
    expect(formatBytes(2_147_483_648, 'en')).toBe('2 GB');
    expect(formatBytes(2048, 'en')).toBe('2 kB');
    expect(formatBytes(12, 'en')).toBe('12 byte');
  });

  it('reads zero in megabytes', () => {
    expect(formatBytes(0, 'en')).toBe('0 MB');
  });
});

describe('formatSpan', () => {
  it('picks the biggest unit in which the span is at least one', () => {
    expect(formatSpan(1_800_000, 'en')).toBe('30 min');
    expect(formatSpan(90_000, 'en')).toBe('1.5 min');
    expect(formatSpan(7_200_000, 'en')).toBe('2 hr');
    expect(formatSpan(500, 'en')).toBe('500 ms');
  });

  it('reads zero in minutes', () => {
    expect(formatSpan(0, 'en')).toBe('0 min');
  });
});
