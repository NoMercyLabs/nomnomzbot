// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useI18n } from 'vue-i18n';

import type { ActionPermission } from '@/api/permissions';

// The area of an action is the first part of its key: moderation:queue:read is in moderation.
export const areaOf = (actionKey: string): string => actionKey.split(/[:.]/)[0] ?? actionKey;

// A search finds an action by its key or by the bot's description of it, in any case.
export function matchesSearch(row: ActionPermission, search: string): boolean {
  const needle = search.trim().toLowerCase();

  if (needle === '')
    return true;

  return `${row.actionKey} ${row.description ?? ''}`.toLowerCase().includes(needle);
}

const isChanged = (row: ActionPermission): boolean => row.overrideLevel != null;

export interface ActionArea {
  area: string;
  label: string;
  rows: ActionPermission[];
  // How many actions of the area the channel moved away from the bot's default.
  changed: number;
}

// An area the bot adds later, with no name here yet, shows its key part instead of a blank row.
export function useAreaLabel(): (area: string) => string {
  const { t, te } = useI18n();

  return (area) => {
    const key = `dashboard.settings.permissions.area.${area}`;

    return te(key)
      ? t(key)
      : area;
  };
}

// The areas in name order, each with its own actions; an area with no action left after the search drops out.
export function groupByArea(
  rows: readonly ActionPermission[],
  labelOf: (area: string) => string,
): ActionArea[] {
  const groups = new Map<string, ActionPermission[]>();

  for (const row of rows) {
    const area = areaOf(row.actionKey);
    const members = groups.get(area);

    if (members === undefined) {
      groups.set(area, [row]);
    } else {
      members.push(row);
    }
  }

  return Array.from(groups, ([area, members]) => ({
    area,
    label: labelOf(area),
    rows: members,
    changed: members.filter(isChanged).length,
  }))
    .sort((left, right) => left.label.localeCompare(right.label));
}
