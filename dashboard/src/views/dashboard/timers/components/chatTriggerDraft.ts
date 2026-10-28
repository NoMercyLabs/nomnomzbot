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

import type { ChatTrigger, ChatTriggerChange } from '@/api/chatTriggers';
import { noPipelineId, type ReplyMode } from '@/components/layout/replyMode';
import { boundTarget, sameTarget, targetToWire, type PickedTarget } from '@/lib/bindingTarget';

export const matchTypes = ['contains', 'exact', 'starts_with', 'regex'] as const;

// What the editor form holds.
export interface ChatTriggerDraft {
  pattern: string;
  matchType: string;
  caseSensitive: boolean;
  mode: ReplyMode;
  response: string;
  target: PickedTarget | null;
  cooldownSeconds: number;
  minPermissionLevel: string;
}

export function draftOf(trigger: ChatTrigger) {
  return {
    pattern: trigger.pattern,
    matchType: trigger.matchType,
    caseSensitive: trigger.caseSensitive,
    mode: boundTarget(trigger.target, trigger.pipelineId) === null
      ? 'simple'
      : 'full',
    response: trigger.response ?? '',
    target: boundTarget(trigger.target, trigger.pipelineId),
    cooldownSeconds: Number(trigger.cooldownSeconds),
    minPermissionLevel: trigger.minPermissionLevel,
  } as const satisfies ChatTriggerDraft;
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

  // The reply text stays on the trigger when a pipeline answers; the bot runs the pipeline first.
  if (draft.mode === 'simple' && draft.response !== saved.response) {
    change.response = draft.response;
  }

  if (draft.mode === 'simple' && saved.target !== null) {
    change.pipelineId = noPipelineId;

    // A command or action binding has no pipeline id to clear, so the target is cleared itself.
    if (saved.target.kind !== 'pipeline') {
      change.target = targetToWire(null);
    }
  }

  if (draft.mode === 'full' && draft.target !== null && !sameTarget(draft.target, saved.target)) {
    change.target = targetToWire(draft.target);
  }

  if (draft.cooldownSeconds !== saved.cooldownSeconds) {
    change.cooldownSeconds = draft.cooldownSeconds;
  }

  if (draft.minPermissionLevel !== saved.minPermissionLevel) {
    change.minPermissionLevel = draft.minPermissionLevel;
  }

  return change;
}
