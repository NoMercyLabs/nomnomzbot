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

import type { WidgetSettingsField } from '@/api/widgets';
import { clampNumber } from '@/views/dashboard/overlays/components/settingField';

function numberField(defaultValue: unknown): WidgetSettingsField {
  return {
    key: 'marginTop',
    label: { key: 'widget.shared.marginTop.label' },
    type: 'number',
    group: { key: 'widget.group.layout' },
    default: defaultValue,
    min: 0,
    max: 2000,
    step: 1,
  };
}

// A side margin starts empty: empty means "use the distance from the edge". Clearing the box must store that
// empty value again, or a streamer who once typed a margin can never go back to the edge offset.
describe('clampNumber', () => {
  it('stores an empty value when the box is cleared and the field starts empty', () => {
    expect(clampNumber('', numberField(''), 32)).toBe('');
  });

  it('keeps the last value when a field that starts with a number is cleared', () => {
    expect(clampNumber('', numberField(32), 48)).toBe(48);
  });

  it('holds a typed number inside min and max', () => {
    expect(clampNumber('2500', numberField(''), 0)).toBe(2000);
    expect(clampNumber('-4', numberField(''), 0)).toBe(0);
    expect(clampNumber('24', numberField(''), 0)).toBe(24);
  });
});
