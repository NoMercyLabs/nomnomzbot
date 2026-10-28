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

import { groupOptions } from '../optionGroups';

describe('groupOptions', () => {
  it('keeps a list with no group as one unlabeled group, in its own order', () => {
    const groups = groupOptions([
      { value: 'b', label: 'Beta' },
      { value: 'a', label: 'Alpha' },
    ]);

    expect(groups).toEqual([
      {
        label: undefined,
        options: [
          { value: 'b', label: 'Beta' },
          { value: 'a', label: 'Alpha' },
        ],
      },
    ]);
  });

  it('gathers the options of one type under one label, groups in the order they first appear', () => {
    const groups = groupOptions([
      {
        value: 'chat:read',
        label: 'Read chat',
        group: 'Chat',
      },
      {
        value: 'bundles:export',
        label: 'Export a bundle',
        group: 'Bundles',
      },
      {
        value: 'chat:send',
        label: 'Send chat messages',
        group: 'Chat',
      },
    ]);

    expect(groups.map((group) => group.label)).toEqual(['Chat', 'Bundles']);
    expect(groups[0]?.options.map((option) => option.value)).toEqual(['chat:read', 'chat:send']);
    expect(groups[1]?.options.map((option) => option.value)).toEqual(['bundles:export']);
  });
});
