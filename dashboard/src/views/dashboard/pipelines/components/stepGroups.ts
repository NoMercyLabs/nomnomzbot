// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The actions of the bot's catalog by category, each group and its rows sorted by name. The step palette and
// the player button editor list actions the same way.
import type { Component } from 'vue';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import type { TextOf } from '@/composables/useLocalizedText';

import { actionIcon } from './stepIcons';
import { actionTitle, type Words } from './stepWords';

export type ActionRow = { type: string; title: string };

export type ActionGroup = {
  key: string;
  heading: string;
  icon: Component;
  rows: ActionRow[];
};

const byTitle = (left: { title: string }, right: { title: string }): number =>
  left.title.localeCompare(right.title);

export function actionGroups(
  actions: PipelineActionDescriptor[],
  words: Words & { text: TextOf },
): ActionGroup[] {
  const found = new Map<string, ActionGroup>();

  for (const action of actions) {
    const key = action.category.key;
    const group = found.get(key) ?? {
      key,
      heading: words.text(action.category, key),
      icon: actionIcon(key),
      rows: [],
    };

    group.rows.push({ type: action.type, title: actionTitle(action.type, words) });
    found.set(key, group);
  }

  return [...found.values()]
    .map((group) =>
      ({ ...group, rows: group.rows.sort(byTitle) }))
    .sort((left: ActionGroup, right: ActionGroup) =>
      left.heading.localeCompare(right.heading));
}
