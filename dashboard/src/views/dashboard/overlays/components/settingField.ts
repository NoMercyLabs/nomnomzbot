// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { JsonObject, JsonValue } from '@/lib/json';
import type { PickerKind } from '@/api/pipelines';
import type { WidgetSettingsField } from '@/api/widgets';
import { numberOrNull } from '@/lib/number';
import { isPickerKind } from '@/views/dashboard/pipelines/components/stepFields';

// A custom overlay may declare a reward or an OBS input as plain text; its key still says what it holds.
const NAMED_PICKERS = [
  { pattern: /rewardid$/i, kind: 'twitch_reward' },
  { pattern: /inputname$/i, kind: 'obs_audio_input' },
] as const satisfies readonly { pattern: RegExp; kind: PickerKind }[];

// The list a text field is picked from: the one the schema names, else the one its key names, else none.
export function pickerOf(field: WidgetSettingsField): PickerKind | null {
  if (field.type !== 'text')
    return null;

  if (field.picker)
    return isPickerKind(field.picker)
      ? field.picker
      : null;

  return NAMED_PICKERS.find((rule) => rule.pattern.test(field.key))?.kind ?? null;
}

// A picked field shows the thing itself (the reward, the input), so the "ID" a declaration names is no word
// a person needs: "Channel point reward ID" reads "Channel point reward", "The ID of the reward" reads "The reward".
export function pickedWords(words: string): string {
  return words
    .replace(/\b(the) ID of (?:the|a|an)\b/gi, '$1')
    .replace(/\s+ID\b/g, '');
}

// A declaration may say its value is edited in the code editor; where the page edits it, that sentence is untrue.
export function withoutCodeEditorNote(words: string): string {
  return words
    .split(/(?<=[.!?])\s+/)
    .filter((sentence) => !/code editor/i.test(sentence))
    .join(' ');
}

// A nested key in plain words: the longest run of its camel-case parts the page has a word for reads in that word
// ("winXpStartupSoundUrl" -> "Windows XP startup sound link"); any other part reads lowercase.
export function plainKey(key: string, wordFor: (part: string) => string | null): string {
  const parts = key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .split(/[\s_.-]+/)
    .filter(Boolean);
  const words: string[] = [];

  for (let start = 0; start < parts.length;) {
    const end = longestKnownRun(parts, start, wordFor);
    const run = parts.slice(start, end).join('').toLowerCase();

    words.push(wordFor(run) ?? run);
    start = end;
  }

  const sentence = words.join(' ');

  return sentence.charAt(0).toUpperCase() + sentence.slice(1);
}

function longestKnownRun(
  parts: string[],
  start: number,
  wordFor: (part: string) => string | null,
): number {
  for (let end = parts.length; end > start + 1; end--) {
    if (wordFor(parts.slice(start, end).join('').toLowerCase()) !== null)
      return end;
  }

  return start + 1;
}

// A JSON setting may arrive as its JSON text (a declared default); read it as the value it spells.
export function parsedJson(value: JsonValue | undefined): JsonValue | undefined {
  if (typeof value !== 'string')
    return value;

  try {
    return JSON.parse(value) as JsonValue;
  } catch {
    return value;
  }
}

export function objectOf(value: JsonValue | undefined): JsonObject | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value
    : null;
}

export function textListOf(value: JsonValue | undefined): string[] | null {
  return Array.isArray(value) && value.every((item): item is string => typeof item === 'string')
    ? value
    : null;
}

// Settings values are plain JSON, so two values are the same when they serialize the same.
export function sameValue(a: JsonValue | undefined, b: JsonValue | undefined): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

// Holds a typed number inside the field's min and max; text that is not a number keeps the old value.
export function clampNumber(
  typed: string,
  field: WidgetSettingsField,
  fallback: JsonValue | undefined,
): JsonValue | undefined {
  const parsed = numberOrNull(typed);

  if (parsed === null)
    return fallback;

  const min = numberOrNull(field.min);
  const max = numberOrNull(field.max);

  if (min !== null && parsed < min)
    return min;

  if (max !== null && parsed > max)
    return max;

  return parsed;
}

// The schema types a catalog default as unknown; it is a JSON value of the field's own type.
export function defaultOf(field: WidgetSettingsField): JsonValue | undefined {
  return field.default as JsonValue | undefined;
}
