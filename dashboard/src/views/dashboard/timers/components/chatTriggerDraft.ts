// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ChatTrigger, ChatTriggerChange } from '@/api/chatTriggers';

export const matchTypes = ['contains', 'exact', 'starts_with', 'regex'] as const;

// The role rungs a trigger can ask for, lowest first.
export const roles = [
  'Everyone',
  'Subscriber',
  'Vip',
  'Artist',
  'Moderator',
  'LeadModerator',
  'Editor',
  'Broadcaster',
] as const;

// What the editor form holds.
export interface ChatTriggerDraft {
  pattern: string;
  matchType: string;
  caseSensitive: boolean;
  response: string;
  cooldownSeconds: number;
  minPermissionLevel: string;
}

export function draftOf(trigger: ChatTrigger): ChatTriggerDraft {
  return {
    pattern: trigger.pattern,
    matchType: trigger.matchType,
    caseSensitive: trigger.caseSensitive,
    response: trigger.response ?? '',
    cooldownSeconds: Number(trigger.cooldownSeconds),
    minPermissionLevel: trigger.minPermissionLevel,
  };
}

// Only the fields that differ from the saved trigger travel to the bot.
export function changeOf(draft: ChatTriggerDraft, trigger: ChatTrigger): ChatTriggerChange {
  const saved = draftOf(trigger);
  const change: ChatTriggerChange = {};

  if (draft.pattern !== saved.pattern) {
    change.pattern = draft.pattern;
  }

  if (draft.matchType !== saved.matchType) {
    change.matchType = draft.matchType;
  }

  if (draft.caseSensitive !== saved.caseSensitive) {
    change.caseSensitive = draft.caseSensitive;
  }

  if (draft.response !== saved.response) {
    change.response = draft.response;
  }

  if (draft.cooldownSeconds !== saved.cooldownSeconds) {
    change.cooldownSeconds = draft.cooldownSeconds;
  }

  if (draft.minPermissionLevel !== saved.minPermissionLevel) {
    change.minPermissionLevel = draft.minPermissionLevel;
  }

  return change;
}
