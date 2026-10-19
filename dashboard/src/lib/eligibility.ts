// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { JsonObject, JsonValue } from './json';

// The media share eligibility rules are stored as JSON text. The one rule the bot enforces is subOnly
// (MediaShareService.CheckEligibilityAsync). The editor shows that rule as a switch and keeps any other key.
function readRules(text: string | null | undefined): JsonObject {
  if (text === null || text === undefined || text.trim() === '')
    return {};

  try {
    const rules: JsonValue = JSON.parse(text);

    return typeof rules === 'object' && rules !== null && !Array.isArray(rules)
      ? (rules as JsonObject)
      : {};
  } catch {
    return {};
  }
}

export const readSubOnly = (text: string | null | undefined): boolean =>
  readRules(text).subOnly === true;

// Empty text tells the bot to clear the rules; null would keep the stored ones.
export function writeSubOnly(text: string | null | undefined, subOnly: boolean): string {
  const { subOnly: _previous, ...others } = readRules(text);
  const rules: JsonObject = subOnly
    ? { ...others, subOnly: true }
    : others;

  return Object.keys(rules).length === 0
    ? ''
    : JSON.stringify(rules);
}
