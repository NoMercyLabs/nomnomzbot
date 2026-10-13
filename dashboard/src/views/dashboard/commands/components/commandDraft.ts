// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { CommandChange, CommandListItem } from '@/api/commands'

import { permissionLevels } from './permissionLevels'

// What the editor form holds. Empty replies means "one reply" mode; a pipeline of '' means none picked.
export interface CommandDraft {
  templateResponse: string
  replies: string[]
  pipelineId: string
  speakWithTts: boolean
  minPermissionLevel: string
  cooldownSeconds: number
  userCooldownSeconds: number
}

// The bot may send the level in any letter case; the form keeps the matching rung of the list.
function canonicalLevel(level: string): string {
  return (
    permissionLevels.find((rung: string): boolean => rung.toLowerCase() === level.toLowerCase()) ??
    level
  )
}

export function draftOf(row: CommandListItem): CommandDraft {
  return {
    templateResponse: row.templateResponse ?? '',
    replies: [...(row.templateResponses ?? [])],
    pipelineId: row.pipelineId ?? '',
    speakWithTts: row.speakWithTts ?? false,
    minPermissionLevel: canonicalLevel(row.minPermissionLevel),
    cooldownSeconds: Number(row.cooldownSeconds),
    userCooldownSeconds: Number(row.userCooldownSeconds),
  }
}

function sameReplies(left: readonly string[], right: readonly string[]): boolean {
  return (
    left.length === right.length &&
    left.every((reply: string, index: number): boolean => reply === right[index])
  )
}

// Only the fields that differ from the row travel to the bot.
export function changeOf(draft: CommandDraft, row: CommandListItem): CommandChange {
  const saved = draftOf(row)
  const change: CommandChange = {}

  if (row.tier === 'template' && draft.templateResponse !== saved.templateResponse) {
    change.templateResponse = draft.templateResponse
  }

  if (row.tier === 'template' && !sameReplies(draft.replies, saved.replies)) {
    change.templateResponses = draft.replies
  }

  if (row.tier === 'pipeline' && draft.pipelineId !== saved.pipelineId && draft.pipelineId !== '') {
    change.pipelineId = draft.pipelineId
  }

  if (draft.speakWithTts !== saved.speakWithTts) {
    change.speakWithTts = draft.speakWithTts
  }

  if (draft.minPermissionLevel !== saved.minPermissionLevel) {
    change.minPermissionLevel = draft.minPermissionLevel
  }

  if (draft.cooldownSeconds !== saved.cooldownSeconds) {
    change.cooldownSeconds = draft.cooldownSeconds
  }

  if (draft.userCooldownSeconds !== saved.userCooldownSeconds) {
    change.userCooldownSeconds = draft.userCooldownSeconds
  }

  return change
}

export function sameDraft(left: CommandDraft, right: CommandDraft): boolean {
  return (
    left.templateResponse === right.templateResponse &&
    sameReplies(left.replies, right.replies) &&
    left.pipelineId === right.pipelineId &&
    left.speakWithTts === right.speakWithTts &&
    left.minPermissionLevel === right.minPermissionLevel &&
    left.cooldownSeconds === right.cooldownSeconds &&
    left.userCooldownSeconds === right.userCooldownSeconds
  )
}
