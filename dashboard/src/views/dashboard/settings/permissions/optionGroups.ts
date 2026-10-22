// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Option } from './components/OptionSelect.vue';

export interface OptionGroup {
  label: string | undefined;
  options: Option[];
}

// A long list of choices reads by type: the options that share a group sit under one label, and the groups
// keep the order in which they first appear. A list with no group stays one plain list.
export function groupOptions(options: readonly Option[]): OptionGroup[] {
  const groups = new Map<string | undefined, Option[]>();

  for (const option of options) {
    const members = groups.get(option.group);

    if (members === undefined) {
      groups.set(option.group, [option]);
    } else {
      members.push(option);
    }
  }

  return Array.from(groups, ([label, members]) => ({ label, options: members }));
}
