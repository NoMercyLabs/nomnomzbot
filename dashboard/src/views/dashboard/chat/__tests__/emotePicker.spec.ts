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

import type { ChatEmoteEntry } from '@/api/chat';

import { favoriteKey, pickerSections, type PickerContext } from '../emotePicker';

// The server marks each emote own, channel, other or global (ChatEmoteOrigin) and names its owner.
function emote(
  code: string,
  provider: string,
  origin: string | null,
  owner: string | null = null,
): ChatEmoteEntry {
  return {
    id: `id-${code}`,
    code,
    provider,
    urls: { 1: `https://cdn.test/${code}.webp` },
    animated: false,
    zeroWidth: false,
    setId: null,
    origin,
    ownerLogin: owner?.toLowerCase() ?? null,
    ownerName: owner,
  };
}

const emotes = [
  emote('zedHi', 'Twitch', 'other', 'Zed'),
  emote('Kappa', 'Twitch', 'global'),
  emote('stoneyWave', 'Twitch', 'channel', 'Stoney_Eagle'),
  emote('adaHype', 'Twitch', 'other', 'Ada'),
  emote('catJAM', 'SevenTv', 'channel', 'SomeUploader'),
  emote('myOwn', 'Twitch', 'own', 'Viewer1'),
  emote('OMEGALUL', 'Bttv', 'global', 'Uploader2'),
  emote('ghostly', 'Twitch', 'other'),
];

const context: PickerContext = { channelName: 'Stoney_Eagle', favorites: [] };

function layout(at: PickerContext = context): [string, string[], string | undefined][] {
  return pickerSections(emotes, [], [], '', at).map((section) =>
    [section.id, section.items.map((item) =>
      item.insert), section.name]);
}

// Stoney 2026-10-14: "group by channel and allow for favorites and prioritize own emotes -> channel emotes ->
// rest on alphabetical channel name -> regular emojis".
describe('the emote picker sections', () => {
  it('puts your own emotes first, then this channel, then other channels by name, then the global ones', () => {
    expect(layout()).toEqual([
      ['own', ['myOwn'], undefined],
      ['channel', ['stoneyWave', 'catJAM'], 'Stoney_Eagle'],
      ['owner-ada', ['adaHype'], 'Ada'],
      ['owner-zed', ['zedHi'], 'Zed'],
      ['others', ['ghostly'], undefined],
      ['global', ['Kappa', 'OMEGALUL'], undefined],
    ]);
  });

  it('shows the favorites above everything, in the order given, and only the ones usable here', () => {
    const favorites = [
      favoriteKey('Bttv', 'id-OMEGALUL'),
      favoriteKey('Twitch', 'id-gone'),
      favoriteKey('Twitch', 'id-zedHi'),
    ];

    expect(layout({ ...context, favorites })[0]).toEqual(['favorites', ['OMEGALUL', 'zedHi'], undefined]);
  });

  it('turns into one list of matches while searching, favorites or not', () => {
    const favorites = [favoriteKey('Twitch', 'id-zedHi')];
    const sections = pickerSections(emotes, [], [], 'ada', { ...context, favorites });

    expect(sections.map((section) =>
      section.id)).toEqual(['results']);
  });
});
