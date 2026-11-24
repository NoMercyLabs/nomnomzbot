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

// A reward definition's payload is the portable part of a channel-point reward (RewardTemplatePayload):
// `{ "title": "...", "cost": 500, "prompt": "...", "response": "...", "isUserInputRequired": false,
// "backgroundColor": "#9146FF", "maxPerStream": 10, "maxPerUserPerStream": 2, "globalCooldownSeconds": 60,
// "timerDurationSeconds": 30, "speaksViaTts": false }`. The form edits every field; a payload with any other key
// is shown as raw JSON, so reading and writing change no part of the payload. A blank optional field is left out.
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';

// The words of the fields belong to the channel points page; this form loads them itself (ensureWords).
export const rewardWords = (locale: string) => import(`../../channel-points/i18n/${locale}.json`);

const KEYS: readonly string[] = [
  'title',
  'cost',
  'prompt',
  'response',
  'isUserInputRequired',
  'backgroundColor',
  'maxPerStream',
  'maxPerUserPerStream',
  'globalCooldownSeconds',
  'timerDurationSeconds',
  'speaksViaTts',
] as const;

// The limits of the bot's reward installer (RewardTemplateInstaller), which are Twitch's limits.
export const rewardLimits = {
  titleMax: 45,
  promptMax: 200,
  responseMax: 2000,
  cooldownMax: 604800,
  timerMax: 86400,
} as const;

export const limitKeys = [
  'maxPerStream',
  'maxPerUserPerStream',
  'globalCooldownSeconds',
  'timerDurationSeconds',
] as const;

export type LimitKey = (typeof limitKeys)[number];

export type RewardPayload = {
  title: string;
  // A number is held as the text of its field; blank means the key is left out.
  cost: string;
  prompt: string;
  response: string;
  isUserInputRequired: boolean;
  backgroundColor: string;
  maxPerStream: string;
  maxPerUserPerStream: string;
  globalCooldownSeconds: string;
  timerDurationSeconds: string;
  speaksViaTts: boolean;
};

export type RewardProblem = 'title' | 'cost' | 'prompt' | 'response' | 'color' | LimitKey;

const hexColor = /^#[0-9A-Fa-f]{6}$/;

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function textOf(value: JsonValue | undefined): string | null {
  if (value === undefined || value === null)
    return '';

  return typeof value === 'string'
    ? value
    : null;
}

function numberTextOf(value: JsonValue | undefined): string | null {
  if (value === undefined || value === null)
    return '';

  return typeof value === 'number'
    ? String(value)
    : null;
}

function flagOf(value: JsonValue | undefined): boolean | null {
  if (value === undefined)
    return false;

  return typeof value === 'boolean'
    ? value
    : null;
}

// The parts of the payload, or null when the payload holds anything the form would drop: another key, text that
// is not JSON, no title or cost, a text that is not text, a number that is not a number, a switch that is not true
// or false. The caller then shows the raw JSON.
export function readRewardPayload(payloadJson: string): RewardPayload | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((name) => KEYS.includes(name)))
    return null;

  const fields = {
    title: typeof parsed.title === 'string' && parsed.title !== ''
      ? parsed.title
      : null,
    cost: typeof parsed.cost === 'number'
      ? String(parsed.cost)
      : null,
    prompt: textOf(parsed.prompt),
    response: textOf(parsed.response),
    isUserInputRequired: flagOf(parsed.isUserInputRequired),
    backgroundColor: textOf(parsed.backgroundColor),
    maxPerStream: numberTextOf(parsed.maxPerStream),
    maxPerUserPerStream: numberTextOf(parsed.maxPerUserPerStream),
    globalCooldownSeconds: numberTextOf(parsed.globalCooldownSeconds),
    timerDurationSeconds: numberTextOf(parsed.timerDurationSeconds),
    speaksViaTts: flagOf(parsed.speaksViaTts),
  };

  if (Object.values(fields).some((field) => field === null))
    return null;

  return fields as RewardPayload;
}

function isWhole(text: string, min: number, max: number): boolean {
  const value = Number(text);

  return text.trim() !== '' && Number.isInteger(value) && value >= min && value <= max;
}

function limitProblem(payload: RewardPayload): LimitKey | null {
  const bounds = {
    maxPerStream: Number.MAX_SAFE_INTEGER,
    maxPerUserPerStream: Number.MAX_SAFE_INTEGER,
    globalCooldownSeconds: rewardLimits.cooldownMax,
    timerDurationSeconds: rewardLimits.timerMax,
  } as const satisfies Record<LimitKey, number>;

  return limitKeys.find((key) => payload[key].trim() !== '' && !isWhole(payload[key], 1, bounds[key])) ?? null;
}

// The first field the bot would refuse, with the same limits as the reward installer.
export function rewardPayloadProblem(payload: RewardPayload): RewardProblem | null {
  if (payload.title.trim() === '' || payload.title.trim().length > rewardLimits.titleMax)
    return 'title';

  if (!isWhole(payload.cost, 1, Number.MAX_SAFE_INTEGER))
    return 'cost';

  if (payload.prompt.length > rewardLimits.promptMax)
    return 'prompt';

  if (payload.response.length > rewardLimits.responseMax)
    return 'response';

  if (payload.backgroundColor !== '' && !hexColor.test(payload.backgroundColor))
    return 'color';

  return limitProblem(payload);
}

// The payload the server reads: a blank optional field is left out, a number is a number again.
export function writeRewardPayload(payload: RewardPayload): string {
  const optional = {
    prompt: payload.prompt || undefined,
    response: payload.response || undefined,
    backgroundColor: payload.backgroundColor || undefined,
    maxPerStream: payload.maxPerStream.trim() === ''
      ? undefined
      : Number(payload.maxPerStream),
    maxPerUserPerStream: payload.maxPerUserPerStream.trim() === ''
      ? undefined
      : Number(payload.maxPerUserPerStream),
    globalCooldownSeconds: payload.globalCooldownSeconds.trim() === ''
      ? undefined
      : Number(payload.globalCooldownSeconds),
    timerDurationSeconds: payload.timerDurationSeconds.trim() === ''
      ? undefined
      : Number(payload.timerDurationSeconds),
  };

  return JSON.stringify({
    title: payload.title,
    cost: Number(payload.cost),
    ...optional,
    isUserInputRequired: payload.isUserInputRequired,
    speaksViaTts: payload.speaksViaTts,
  });
}
