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

import { useI18n } from 'vue-i18n';

import type { ActionPermission } from '@/api/permissions';

// The area of an action is the first part of its key: moderation:queue:read is in moderation.
export const areaOf = (actionKey: string): string => actionKey.split(/[:.]/)[0] ?? actionKey;

// The message name of an action's plain-words name: every run of signs becomes one underscore,
// so moderation:delete_message reads moderation_delete_message.
export const actionNameSlug = (actionKey: string): string => actionKey.replace(/[^a-z0-9]+/gi, '_');

// A search finds an action by its plain name, its key or the bot's description of it, in any case.
export function matchesSearch(
  row: ActionPermission,
  search: string,
  nameOf: (row: ActionPermission) => string,
): boolean {
  const needle = search.trim().toLowerCase();

  if (needle === '')
    return true;

  return `${nameOf(row)} ${row.actionKey} ${row.description ?? ''}`.toLowerCase().includes(needle);
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

export interface NamedAction {
  actionKey: string;
  description?: string | null;
}

// A person reads an action by its plain name. An action the bot adds later, with no name here yet,
// shows the bot's description, else the area it sits in: never its key.
export function useActionName(): (action: NamedAction) => string {
  const { t, te } = useI18n();
  const labelOf = useAreaLabel();

  return ({ actionKey, description }) => {
    const key = `dashboard.settings.permissions.action.${actionNameSlug(actionKey)}`;

    if (te(key))
      return t(key);

    if (description != null && description.trim() !== '')
      return description;

    return t('dashboard.settings.permissions.matrix.unnamed', { area: labelOf(areaOf(actionKey)) });
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
