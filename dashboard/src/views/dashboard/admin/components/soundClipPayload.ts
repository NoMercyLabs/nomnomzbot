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

// A sound clip definition's payload is the portable part of a sound clip (SoundClipTemplatePayload):
// `{ "name": "raid-horn", "displayName": "Raid horn", "assetId": "...", "defaultVolume": 70, "cooldownSeconds": 30,
// "minPermissionLevel": "Subscriber", "triggerWord": "horn" }`. The form edits every field with the channel's own
// sound clip form state; a payload with any other key is shown as raw JSON, so reading and writing change no part
// of the payload. A blank trigger word is left out.
import { rungNames } from '@/api/permissions';
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';
import {
  editProblem,
  type EditForm,
  type SoundClipProblem,
} from '@/views/dashboard/sounds/soundClipForm';

// The words of the fields belong to the sounds page; this form loads them itself (ensureWords).
export const soundClipWords = (locale: string) => import(`../../sounds/i18n/${locale}.json`);

const KEYS: readonly string[] = [
  'name',
  'displayName',
  'assetId',
  'defaultVolume',
  'cooldownSeconds',
  'minPermissionLevel',
  'triggerWord',
] as const;

// The limits the installer holds: a name of letters, numbers, underscores and hyphens, and a cooldown of a day.
const namePattern = /^[A-Za-z0-9_-]+$/;
const maxNameLength = 50;
const maxDisplayNameLength = 100;
const maxCooldownSeconds = 86_400;

export type SoundClipPayloadForm = EditForm & {
  name: string;
  assetId: string;
};

export type SoundClipPayloadProblem =
  | SoundClipProblem |
  'nameRequired' |
  'nameCharacters' |
  'nameTooLong' |
  'displayNameTooLong' |
  'cooldownTooLong' |
  'assetRequired';

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function textOr(value: JsonValue | undefined, fallback: string): string | null {
  if (value === undefined || value === null)
    return fallback;

  return typeof value === 'string'
    ? value
    : null;
}

function numberOr(value: JsonValue | undefined, fallback: number): number | null {
  if (value === undefined || value === null)
    return fallback;

  return typeof value === 'number'
    ? value
    : null;
}

// The parts of the payload, or null when the payload holds anything the form would drop: another key, text that
// is not JSON, a field of the wrong type, a permission the list does not know. The caller then shows the raw JSON.
export function readSoundClipPayload(payloadJson: string): SoundClipPayloadForm | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((key) => KEYS.includes(key)))
    return null;

  const name = textOr(parsed.name, '');
  const displayName = textOr(parsed.displayName, '');
  const assetId = textOr(parsed.assetId, '');
  const defaultVolume = numberOr(parsed.defaultVolume, 80);
  const cooldownSeconds = numberOr(parsed.cooldownSeconds, 0);
  const minPermissionLevel = textOr(parsed.minPermissionLevel, 'Everyone');
  const triggerWord = textOr(parsed.triggerWord, '');

  if (name === null || displayName === null || assetId === null || triggerWord === null)
    return null;

  if (defaultVolume === null || cooldownSeconds === null || minPermissionLevel === null)
    return null;

  if (!rungNames.includes(minPermissionLevel))
    return null;

  return {
    name,
    displayName,
    assetId,
    defaultVolume,
    cooldownSeconds,
    minPermissionLevel,
    triggerWord,
  };
}

function nameProblem(name: string): SoundClipPayloadProblem | null {
  if (name === '')
    return 'nameRequired';

  if (name.length > maxNameLength)
    return 'nameTooLong';

  return namePattern.test(name)
    ? null
    : 'nameCharacters';
}

// The first field the installer would refuse: the limits of the channel's own form, then the ones the template adds.
export function soundClipPayloadProblem(
  form: SoundClipPayloadForm,
): SoundClipPayloadProblem | null {
  const own = nameProblem(form.name.trim());

  if (own !== null)
    return own;

  if (form.displayName.trim().length > maxDisplayNameLength)
    return 'displayNameTooLong';

  if (form.assetId === '')
    return 'assetRequired';

  if (form.cooldownSeconds > maxCooldownSeconds)
    return 'cooldownTooLong';

  return editProblem(form) ?? null;
}

// The payload the server reads: trimmed texts, the trigger word left out when blank.
export function writeSoundClipPayload(form: SoundClipPayloadForm): string {
  const trigger = form.triggerWord.trim();

  return JSON.stringify({
    name: form.name.trim(),
    displayName: form.displayName.trim(),
    assetId: form.assetId,
    defaultVolume: form.defaultVolume,
    cooldownSeconds: form.cooldownSeconds,
    minPermissionLevel: form.minPermissionLevel,
    triggerWord: trigger === ''
      ? undefined
      : trigger,
  });
}
