// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { TemplateHelper } from '@/api/templates';
import { merge } from '@/lib/array';

export function braced(key: string): string {
  return `{${key}}`;
}

export interface OpenPlaceholder {
  /** Index of the opening brace. */
  from: number;
  /** What was typed after the brace. */
  query: string;
}

// The brace the caret is inside, if the person is still writing its name.
export function openPlaceholder(text: string, caret: number) {
  const match: RegExpExecArray | null = /\{([\w.<>:|-]*)$/.exec(text.slice(0, caret));

  return match
    ? { from: match.index, query: match[1] ?? '' } as const satisfies OpenPlaceholder
    : null;
}

// Helpers that start with the query come first, then those that only contain it.
export function filterHelpers(
  helpers: readonly TemplateHelper[],
  query: string,
): readonly TemplateHelper[] {
  const needle = query.toLowerCase();
  const starts = helpers.filter((helper) =>
    helper.key.toLowerCase().startsWith(needle),
  );
  const contains = helpers.filter(
    (helper) =>
      !helper.key.toLowerCase().startsWith(needle) && helper.key.toLowerCase().includes(needle),
  );

  return merge(starts, contains);
}

// A key such as "args.<n>" stands for a family: every name that starts with "args." and goes on.
function matches(helper: TemplateHelper, name: string): boolean {
  const key = helper.key.toLowerCase();
  const open = key.indexOf('<');

  if (open === -1)
    return key === name;

  const prefix: string = key.slice(0, open);

  return name.length > prefix.length && name.startsWith(prefix);
}

// Every {name} in the text that no helper of this place accepts, each once, in written order.
export function unknownPlaceholders(text: string, helpers: TemplateHelper[]): string[] {
  const unknown: string[] = [];

  for (const match of text.matchAll(/\{([^{}\s][^{}]*)\}/g)) {
    const name: string = (match[1] ?? '').trim().toLowerCase();

    if (helpers.some((helper) => matches(helper, name))) continue;

    if (!unknown.includes(match[0])) {
      unknown.push(match[0]);
    }
  }

  return unknown;
}

// The panel groups helpers by the first part of the key: user, user.name and user.id are all "user".
export function helperGroup(key: string): string {
  return key.split(/[.:<]/)[0] ?? key;
}

export function groupHelpers(
  helpers: readonly TemplateHelper[],
): Array<{ group: string; helpers: TemplateHelper[] }> {
  const groups = new Map<string, TemplateHelper[]>();

  for (const helper of helpers) {
    const group = helperGroup(helper.key);
    const grouped = groups.get(group) ?? [];

    grouped.push(helper);
    groups.set(group, grouped);
  }

  return [...groups].map(([group, grouped]) => ({
    group,
    helpers: grouped,
  }));
}

// The panel search: a hit in the key comes first (key start before key anywhere), then a hit in the
// description or the sample. An empty query keeps every helper in the order the bot sent.
export function searchHelpers(
  helpers: readonly TemplateHelper[],
  query: string,
  describe: (helper: TemplateHelper) => string,
): readonly TemplateHelper[] {
  const needle = query.toLowerCase();

  if (needle === '')
    return helpers;

  const byKey = filterHelpers(helpers, needle);
  const inText = helpers.filter((helper) => {
    const words = `${describe(helper)} ${helper.sample}`.toLowerCase();

    return !helper.key.toLowerCase().includes(needle) && words.includes(needle);
  });

  return merge(byKey, inText);
}

// A group shows this many rows before its "show more" button.
export const groupRowLimit = 60;
