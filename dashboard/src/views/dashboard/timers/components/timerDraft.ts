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

// What the editor form holds. The messages are one text, one message per line.
export interface TimerDraft {
  name: string;
  messages: string;
  intervalMinutes: number;
  minChatActivity: number;
  fireOnce: boolean;
  speakWithTts: boolean;
}

// The bot stores a list; the form edits it as lines. Blank lines are not messages.
export function messagesOf(lines: string): string[] {
  return lines
    .split('\n')
    .map((line: string): string => line.trim())
    .filter((line: string): boolean => line !== '');
}

export function draftOf(timer: Timer): TimerDraft {
  return {
    name: timer.name,
    messages: timer.messages.join('\n'),
    intervalMinutes: Number(timer.intervalMinutes),
    minChatActivity: Number(timer.minChatActivity),
    fireOnce: timer.fireOnce,
    speakWithTts: timer.speakWithTts ?? false,
  };
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

  return change;
}
