// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { CommandChange, CommandListItem } from '@/api/commands';

import type { ReplyMode } from '@/components/layout/replyMode';

import { rungNames } from '@/api/permissions';

export const prefixModes = ['Default', 'Custom', 'None'] as const;
export const matchModes = ['StartsWith', 'Exact', 'Contains', 'Regex'] as const;

export type PrefixMode = (typeof prefixModes)[number];
export type MatchMode = (typeof matchModes)[number];

// The bot sends the mode as text; a value this list does not know falls back to the default.
function prefixModeOf(raw: string): PrefixMode {
  return prefixModes.find((mode): boolean => mode === raw) ?? 'Default';
}

function matchModeOf(raw: string): MatchMode {
  return matchModes.find((mode): boolean => mode === raw) ?? 'StartsWith';
}

export type ReplyKind = 'text' | 'random' | 'code';

// What the editor form holds; a pipeline or a script of '' means none picked. The kind says which of the
// text, the replies and the script answers. All stay in the draft, so a look at another kind never throws one away.
export interface CommandDraft {
  prefixMode: PrefixMode;
  customPrefix: string;
  matchMode: MatchMode;
  matchPattern: string;
  mode: ReplyMode;
  kind: ReplyKind;
  templateResponse: string;
  replies: string[];
  pipelineId: string;
  codeScriptId: string;
  speakWithTts: boolean;
  minPermissionLevel: string;
  cooldownSeconds: number;
  userCooldownSeconds: number;
}

function kindOf(row: CommandListItem): ReplyKind {
  if (row.tier === 'code')
    return 'code';

  return (row.templateResponses ?? []).length > 0
    ? 'random'
    : 'text';
}

// The bot may send the level in any letter case; the form keeps the matching rung of the list.
function canonicalLevel(level: string): string {
  return (
    rungNames.find((rung): boolean => rung.toLowerCase() === level.toLowerCase()) ??
    level
  );
}

export function draftOf(row: CommandListItem): CommandDraft {
  return {
    prefixMode: prefixModeOf(row.prefixMode),
    customPrefix: row.customPrefix ?? '',
    matchMode: matchModeOf(row.matchMode),
    matchPattern: row.matchPattern ?? '',
    mode: row.tier === 'pipeline'
      ? 'full'
      : 'simple',
    kind: kindOf(row),
    templateResponse: row.templateResponse ?? '',
    replies: [...(row.templateResponses ?? [])],
    pipelineId: row.pipelineId ?? '',
    codeScriptId: row.codeScriptId ?? '',
    speakWithTts: row.speakWithTts ?? false,
    minPermissionLevel: canonicalLevel(row.minPermissionLevel),
    cooldownSeconds: Number(row.cooldownSeconds),
    userCooldownSeconds: Number(row.userCooldownSeconds),
  };
}

function sameReplies(left: readonly string[], right: readonly string[]): boolean {
  return (
    left.length === right.length &&
    left.every((reply, index): boolean => reply === right[index])
  );
}

// Only the fields that differ from the row travel to the bot.
export function changeOf(draft: CommandDraft, row: CommandListItem): CommandChange {
  const saved = draftOf(row);
  const change: CommandChange = {};

  const replyTier = row.tier === 'template' || row.tier === 'pipeline' || row.tier === 'code';
  const simple = replyTier && draft.mode === 'simple' && draft.kind !== 'code';
  const full = replyTier && draft.mode === 'full' && draft.pipelineId !== '';
  const code = replyTier && draft.mode === 'simple' && draft.kind === 'code' && draft.codeScriptId !== '';

  // The reply text stays on the command when it runs a pipeline; only the tier says which one answers.
  if (simple && draft.kind === 'text' && draft.templateResponse !== saved.templateResponse) {
    change.templateResponse = draft.templateResponse;
  }

  // A text command sends no replies: the bot answers with the list whenever it has one.
  const replies: string[] = draft.kind === 'random'
    ? draft.replies
    : [];

  if (simple && !sameReplies(replies, saved.replies)) {
    change.templateResponses = replies;
  }

  if (simple && row.tier !== 'template') {
    change.tier = 'template';
  }

  if (full && (row.tier !== 'pipeline' || draft.pipelineId !== saved.pipelineId)) {
    change.tier = 'pipeline';
    change.pipelineId = draft.pipelineId;
  }

  // A script runs the command itself: the tier and the script travel, never a pipeline.
  if (code && (row.tier !== 'code' || draft.codeScriptId !== saved.codeScriptId)) {
    change.tier = 'code';
    change.codeScriptId = draft.codeScriptId;
  }

  // A pipeline or a script speaks through its own steps, so the command-level switch is off while one runs.
  const speaks = draft.mode === 'full' || draft.kind === 'code'
    ? false
    : draft.speakWithTts;

  if (speaks !== saved.speakWithTts) {
    change.speakWithTts = speaks;
  }

  if (draft.prefixMode !== saved.prefixMode) {
    change.prefixMode = draft.prefixMode;
  }

  const customPrefixChanged = draft.customPrefix !== saved.customPrefix;

  if (draft.prefixMode === 'Custom' && (draft.prefixMode !== saved.prefixMode || customPrefixChanged)) {
    change.customPrefix = draft.customPrefix;
  }

  if (draft.matchMode !== saved.matchMode) {
    change.matchMode = draft.matchMode;
  }

  const patternChanged = draft.matchPattern !== saved.matchPattern;

  if (draft.matchMode === 'Regex' && (draft.matchMode !== saved.matchMode || patternChanged)) {
    change.matchPattern = draft.matchPattern;
  }

  if (draft.minPermissionLevel !== saved.minPermissionLevel) {
    change.minPermissionLevel = draft.minPermissionLevel;
  }

  if (draft.cooldownSeconds !== saved.cooldownSeconds) {
    change.cooldownSeconds = draft.cooldownSeconds;
  }

  if (draft.userCooldownSeconds !== saved.userCooldownSeconds) {
    change.userCooldownSeconds = draft.userCooldownSeconds;
  }

  return change;
}

export function sameDraft(left: CommandDraft, right: CommandDraft): boolean {
  return (
    left.prefixMode === right.prefixMode &&
    left.customPrefix === right.customPrefix &&
    left.matchMode === right.matchMode &&
    left.matchPattern === right.matchPattern &&
    left.mode === right.mode &&
    left.kind === right.kind &&
    left.templateResponse === right.templateResponse &&
    sameReplies(left.replies, right.replies) &&
    left.pipelineId === right.pipelineId &&
    left.codeScriptId === right.codeScriptId &&
    left.speakWithTts === right.speakWithTts &&
    left.minPermissionLevel === right.minPermissionLevel &&
    left.cooldownSeconds === right.cooldownSeconds &&
    left.userCooldownSeconds === right.userCooldownSeconds
  );
}

// Custom needs a prefix and Regex needs a pattern; the bot refuses either one empty.
export function triggerValid(draft: CommandDraft): boolean {
  const prefixOk = draft.prefixMode !== 'Custom' || draft.customPrefix !== '';
  const patternOk = draft.matchMode !== 'Regex' || draft.matchPattern !== '';

  return prefixOk && patternOk;
}
