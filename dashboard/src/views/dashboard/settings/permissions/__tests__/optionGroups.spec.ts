// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
      { value: 'chat:read', label: 'Read chat', group: 'Chat' },
      { value: 'bundles:export', label: 'Export a bundle', group: 'Bundles' },
      { value: 'chat:send', label: 'Send chat messages', group: 'Chat' },
    ]);

    expect(groups.map((group) => group.label)).toEqual(['Chat', 'Bundles']);
    expect(groups[0]?.options.map((option) => option.value)).toEqual(['chat:read', 'chat:send']);
    expect(groups[1]?.options.map((option) => option.value)).toEqual(['bundles:export']);
  });
});
