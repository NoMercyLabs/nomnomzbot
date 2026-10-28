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
