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

// A pick list definition's payload is the portable part of a pick list (PickListTemplatePayload):
// `{ "name": "Snacks", "description": "...", "items": ["Apple", "Cheese"] }`. The form edits every field with the
// channel's own pick list form state; a payload with any other key is shown as raw JSON, so reading and writing
// change no part of the payload. A blank description is left out.
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';
import {
  draftFromForm,
  formProblem,
  type PickListForm,
  type PickListProblem,
} from '@/views/dashboard/picklists/pickListForm';

// The words of the fields belong to the pick lists page; this form loads them itself (ensureWords).
export const pickListWords = (locale: string) => import(`../../picklists/i18n/${locale}.json`);

const KEYS: readonly string[] = ['name', 'description', 'items'] as const;

// The installer only accepts a name of letters, numbers, underscores and hyphens, and at least one entry.
const namePattern = /^[A-Za-z0-9_-]+$/;

export type PickListPayloadProblem = PickListProblem | 'nameCharacters' | 'itemsRequired';

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function descriptionOf(value: JsonValue | undefined): string | null {
  if (value === undefined || value === null)
    return '';

  return typeof value === 'string'
    ? value
    : null;
}

// An entry the form would change when it splits the text into lines: empty, padded or holding a line break.
function isWholeEntry(item: JsonValue): item is string {
  return typeof item === 'string' && item !== '' && item === item.trim() && !item.includes('\n');
}

// The parts of the payload, or null when the payload holds anything the form would drop: another key, text that
// is not JSON, a name that is not text (a blank name is left to the form's own check), a description that is not text, entries that are not plain one-line text. The caller then
// shows the raw JSON.
export function readPickListPayload(payloadJson: string): PickListForm | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((key) => KEYS.includes(key)))
    return null;

  const description = descriptionOf(parsed.description);
  const items = parsed.items;

  if (typeof parsed.name !== 'string' || description === null)
    return null;

  if (!Array.isArray(items) || !items.every(isWholeEntry))
    return null;

  return {
    name: parsed.name,
    description,
    items: items.join('\n'),
  };
}

// The first field the installer would refuse: the limits of the channel's own form, then the two the template adds.
export function pickListPayloadProblem(form: PickListForm): PickListPayloadProblem | null {
  const problem = formProblem(form);

  if (problem !== undefined)
    return problem;

  if (!namePattern.test(form.name.trim()))
    return 'nameCharacters';

  return draftFromForm(form).items.length === 0
    ? 'itemsRequired'
    : null;
}

// The payload the server reads: the trimmed name, the description left out when blank, one entry per line.
export function writePickListPayload(form: PickListForm): string {
  const { name, description, items } = draftFromForm(form);

  return JSON.stringify({
    name,
    description: description ?? undefined,
    items,
  });
}
