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

// A timer definition's payload is the portable part of a channel's timer:
// `{ "name": "...", "messages": ["..."], "intervalMinutes": 30, "minChatActivity": 0, "fireOnce": false,
// "isEnabled": true }`. A timer with no messages runs a pipeline that the installing channel picks, so the form
// keeps an empty message list as it is. The form edits every field; a payload with any other key is shown as raw
// JSON, so reading and writing change no part of the payload.
import type { JsonValue } from '@/lib/json';
import { objectOf } from '@/views/dashboard/overlays/components/settingField';
import { defaultMinutes, messagesOf, timerLimits } from '@/views/dashboard/timers/components/timerDraft';

// The words of the fields belong to the timers page; this form loads them itself (ensureWords).
export const timerWords = (locale: string) => import(`../../timers/i18n/${locale}.json`);

const KEYS: readonly string[] = [
  'name',
  'messages',
  'intervalMinutes',
  'minChatActivity',
  'fireOnce',
  'isEnabled',
] as const;

export type TimerPayload = {
  name: string;
  // One message per line, as the channel timer form edits them.
  messages: string;
  intervalMinutes: number;
  minChatActivity: number;
  fireOnce: boolean;
  isEnabled: boolean;
};

function parseJson(payloadJson: string): JsonValue | undefined {
  try {
    return JSON.parse(payloadJson) as JsonValue;
  } catch {
    return undefined;
  }
}

function linesOf(value: JsonValue | undefined): string | null {
  const list = value ?? [];

  if (!Array.isArray(list) || !list.every((entry) => typeof entry === 'string' && entry.trim() !== ''))
    return null;

  return list.join('\n');
}

// The parts of the payload, or null when the payload holds anything the form would drop: another key, text that
// is not JSON, no name, messages that are not plain text lines, a number that is not a number, a switch that is
// not true or false. The caller then shows the raw JSON.
export function readTimerPayload(payloadJson: string): TimerPayload | null {
  const parsed = objectOf(parseJson(payloadJson));

  if (parsed === null || !Object.keys(parsed).every((name) => KEYS.includes(name)))
    return null;

  const messages = linesOf(parsed.messages);
  const intervalMinutes = parsed.intervalMinutes ?? defaultMinutes;
  const minChatActivity = parsed.minChatActivity ?? 0;
  const fireOnce = parsed.fireOnce ?? false;
  const isEnabled = parsed.isEnabled ?? true;

  if (
    typeof parsed.name !== 'string' ||
    parsed.name === '' ||
    messages === null ||
    typeof intervalMinutes !== 'number' ||
    typeof minChatActivity !== 'number' ||
    typeof fireOnce !== 'boolean' ||
    typeof isEnabled !== 'boolean'
  )
    return null;

  return {
    name: parsed.name,
    messages,
    intervalMinutes,
    minChatActivity,
    fireOnce,
    isEnabled,
  };
}

// The first number the bot would refuse, with the same limits as the channel timer form.
export function timerPayloadProblem(payload: TimerPayload): 'interval' | 'activity' | null {
  const minutes = payload.intervalMinutes;
  const lines = payload.minChatActivity;

  if (
    !Number.isInteger(minutes) ||
    minutes < timerLimits.minMinutes ||
    minutes > timerLimits.maxMinutes
  )
    return 'interval';

  if (!Number.isInteger(lines) || lines < 0 || lines > timerLimits.maxActivity)
    return 'activity';

  return null;
}

// The payload the server reads: the message lines become a list again, blank lines left out.
export function writeTimerPayload(payload: TimerPayload): string {
  return JSON.stringify({
    name: payload.name,
    messages: messagesOf(payload.messages),
    intervalMinutes: payload.intervalMinutes,
    minChatActivity: payload.minChatActivity,
    fireOnce: payload.fireOnce,
    isEnabled: payload.isEnabled,
  });
}
