// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Timer, TimerChange } from '@/api/timers';
import { boundTarget, sameTarget, targetToWire, type PickedTarget } from '@/lib/bindingTarget';

// What the editor form holds. The messages are one text, one message per line.
export interface TimerDraft {
  name: string;
  messages: string;
  intervalMinutes: number;
  minChatActivity: number;
  fireOnce: boolean;
  speakWithTts: boolean;
  target: PickedTarget | null;
}

// The bot's own limits and the interval a new timer starts at.
export const defaultMinutes = 30;

// A new timer needs no chat lines: it posts on the interval alone.
export const defaultChatLines = 0;

export const timerLimits = {
  minMinutes: 1,
  maxMinutes: 1440,
  maxActivity: 10000,
} as const;

export type TimerProblem = 'interval' | 'activity';

// The first field the bot would refuse, so the form says so before it sends.
export function problemOf(draft: TimerDraft): TimerProblem | null {
  const minutes = draft.intervalMinutes;
  const lines = draft.minChatActivity;

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

// The bot stores a list; the form edits it as lines. Blank lines are not messages.
export function messagesOf(lines: string): string[] {
  return lines
    .split('\n')
    .map((line): string => line.trim())
    .filter((line): boolean => line !== '');
}

export function draftOf(timer: Timer) {
  return {
    name: timer.name,
    messages: timer.messages.join('\n'),
    intervalMinutes: Number(timer.intervalMinutes),
    minChatActivity: Number(timer.minChatActivity),
    fireOnce: timer.fireOnce,
    speakWithTts: timer.speakWithTts ?? false,
    target: boundTarget(timer.target, timer.pipelineId),
  } as const satisfies TimerDraft;
}

export function sameDraft(left: TimerDraft, right: TimerDraft): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

// Only the fields that differ from the saved timer travel to the bot.
export function changeOf(draft: TimerDraft, timer: Timer): TimerChange {
  const saved = draftOf(timer);
  const change: TimerChange = {};

  if (draft.name !== saved.name) {
    change.name = draft.name;
  }

  if (messagesOf(draft.messages).join('\n') !== messagesOf(saved.messages).join('\n')) {
    change.messages = messagesOf(draft.messages);
  }

  if (draft.intervalMinutes !== saved.intervalMinutes) {
    change.intervalMinutes = draft.intervalMinutes;
  }

  if (draft.minChatActivity !== saved.minChatActivity) {
    change.minChatActivity = draft.minChatActivity;
  }

  if (draft.fireOnce !== saved.fireOnce) {
    change.fireOnce = draft.fireOnce;
  }

  if (draft.speakWithTts !== saved.speakWithTts) {
    change.speakWithTts = draft.speakWithTts;
  }

  // A cleared binding is sent as the empty target; a left-alone one is not sent at all.
  if (!sameTarget(draft.target, saved.target)) {
    change.target = targetToWire(draft.target);
  }

  return change;
}
