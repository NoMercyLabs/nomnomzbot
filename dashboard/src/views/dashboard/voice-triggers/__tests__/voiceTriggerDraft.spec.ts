// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import type { VoiceTrigger } from '@/api/voiceTriggers';

import { changeOf, draftOf, emptyStickerId, noSticker } from '../components/voiceTriggerDraft';

const saved = {
  id: 'a',
  word: 'nomnom',
  isEnabled: true,
  startingCount: 0,
  currentCount: 3,
  cooldownSeconds: 5,
  stickerAssetId: 'sticker-1',
  createdAt: '2026-10-01T10:00:00Z',
  updatedAt: '2026-10-01T10:00:00Z',
} as VoiceTrigger;

describe('voice trigger draft', () => {
  it('sends nothing when the draft equals the saved trigger', () => {
    expect(changeOf(draftOf(saved), saved)).toEqual({});
  });

  it('opens a trigger without a sticker on the no-sticker choice', () => {
    expect(draftOf({ ...saved, stickerAssetId: null }).stickerAssetId).toBe(noSticker);
  });

  it('sends the empty id to clear a sticker', () => {
    const draft = {
      ...draftOf(saved),
      stickerAssetId: noSticker,
    };

    expect(changeOf(draft, saved)).toEqual({ stickerAssetId: emptyStickerId });
  });

  it('sends a trimmed word and a new sticker id as the only changes', () => {
    const draft = {
      ...draftOf(saved),
      word: '  yum  ',
      stickerAssetId: 'sticker-2',
    };

    expect(changeOf(draft, saved)).toEqual({ word: 'yum', stickerAssetId: 'sticker-2' });
  });
});
