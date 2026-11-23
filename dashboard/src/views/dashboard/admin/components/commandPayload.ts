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

// A command definition's payload is the built-in overrides blob the server copies into a channel:
// `{ "responses": { "<slot>": "<template>" }, "speakWithTts": true, "cooldownSeconds": 30,
// "userCooldownSeconds": 5, "minPermissionLevel": 10 }`. Every field is optional and a missing one means "the
// catalog default". The form edits these fields the way the channel's built-in editor does, so reading and
// writing change no part of the payload; the level travels as the number of its rung.
import { rungs } from '@/api/permissions';
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';

// The words of the fields belong to the commands page; this form loads them itself (ensureWords).
export const commandWords = (locale: string) => import(`../../commands/i18n/${locale}.json`);

const KEYS: readonly string[] = [
  'responses',
  'speakWithTts',
  'cooldownSeconds',
  'userCooldownSeconds',
  'minPermissionLevel',
] as const;

export type CommandPayload = {
  // The templates the payload sets, by slot. A slot the catalog does not list stays here untouched.
  responses: Record<string, string>;
  speakWithTts: boolean;
  cooldownSeconds: number | null;
  userCooldownSeconds: number | null;
  // The rung name, or null for the catalog floor.
  level: string | null;
};

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function responsesOf(value: JsonValue | undefined): Record<string, string> | null {
  const map = objectOf(value ?? {});

  if (map === null || !Object.values(map).every((entry) => typeof entry === 'string'))
    return null;

  return map as Record<string, string>;
}

// A cooldown the server would read: a whole number of seconds, zero or more.
function secondsOf(value: JsonValue | undefined): number | null | undefined {
  if (value === undefined)
    return null;

  return typeof value === 'number' && Number.isInteger(value) && value >= 0
    ? value
    : undefined;
}

// The parts of the payload, or null when the payload holds anything the form would drop: another field, text
// that is not JSON, replies that are not text, a cooldown that is not a whole number, a level that is not a
// rung. The caller then shows the raw JSON. `{}` reads as every default.
export function readCommandPayload(payloadJson: string): CommandPayload | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((name) => KEYS.includes(name)))
    return null;

  const responses = responsesOf(parsed.responses);
  const cooldownSeconds = secondsOf(parsed.cooldownSeconds);
  const userCooldownSeconds = secondsOf(parsed.userCooldownSeconds);
  const rung = rungs.find((candidate) => candidate.level === parsed.minPermissionLevel);
  const speak = parsed.speakWithTts ?? false;
  const levelGiven = parsed.minPermissionLevel !== undefined;

  if (
    responses === null ||
    cooldownSeconds === undefined ||
    userCooldownSeconds === undefined ||
    typeof speak !== 'boolean' ||
    (levelGiven && rung === undefined)
  )
    return null;

  return {
    responses,
    speakWithTts: speak,
    cooldownSeconds,
    userCooldownSeconds,
    level: rung?.name ?? null,
  };
}

// The overrides blob: only the fields that are set, so a field at its default is not written.
export function writeCommandPayload(payload: CommandPayload): string {
  const rung = rungs.find((candidate) => candidate.name === payload.level);
  const hasResponses = Object.keys(payload.responses).length > 0;

  return JSON.stringify({
    responses: hasResponses
      ? payload.responses
      : undefined,
    speakWithTts: payload.speakWithTts
      ? true
      : undefined,
    cooldownSeconds: payload.cooldownSeconds ?? undefined,
    userCooldownSeconds: payload.userCooldownSeconds ?? undefined,
    minPermissionLevel: rung?.level,
  });
}
