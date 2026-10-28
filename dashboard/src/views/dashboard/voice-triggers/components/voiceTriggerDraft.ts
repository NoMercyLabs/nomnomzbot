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
