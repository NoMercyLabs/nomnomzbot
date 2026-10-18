// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { VoiceTrigger, VoiceTriggerChange } from '@/api/voiceTriggers';

// The Select value for "no sticker"; the bot clears a sticker when it gets the empty id.
export const noSticker = 'none';
export const emptyStickerId = '00000000-0000-0000-0000-000000000000';

// What the editor form holds.
export interface VoiceTriggerDraft {
  word: string;
  cooldownSeconds: number;
  stickerAssetId: string;
}

export function draftOf(trigger: VoiceTrigger) {
  return {
    word: trigger.word,
    cooldownSeconds: Number(trigger.cooldownSeconds),
    stickerAssetId: trigger.stickerAssetId ?? noSticker,
  } as const satisfies VoiceTriggerDraft;
}

// Only the fields that differ from the saved trigger travel to the bot. The Enabled switch saves on its own.
export function changeOf(draft: VoiceTriggerDraft, trigger: VoiceTrigger): VoiceTriggerChange {
  const saved = draftOf(trigger);
  const change: VoiceTriggerChange = {};

  if (draft.word.trim() !== saved.word) {
    change.word = draft.word.trim();
  }

  if (draft.cooldownSeconds !== saved.cooldownSeconds) {
    change.cooldownSeconds = draft.cooldownSeconds;
  }

  if (draft.stickerAssetId !== saved.stickerAssetId) {
    change.stickerAssetId = draft.stickerAssetId === noSticker
      ? emptyStickerId
      : draft.stickerAssetId;
  }

  return change;
}
