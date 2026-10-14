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

import { favoriteKey, pickerSections, skinTone, type PickerContext } from '../emotePicker';

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
  emote('LULW', 'Ffz', 'global'),
  emote('PepeLaugh', 'SevenTv', 'global'),
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
      ['others-Twitch', ['ghostly'], undefined],
      ['global-Twitch', ['Kappa'], undefined],
      ['global-SevenTv', ['PepeLaugh'], undefined],
      ['global-Bttv', ['OMEGALUL'], undefined],
      ['global-Ffz', ['LULW'], undefined],
    ]);
  });

  // Stoney 2026-10-14: "where did you leave the frankerfacez and the other tabs?"
  it('keeps a section per source when the server does not say whose an emote is', () => {
    const unowned = [emote('Kappa', 'Twitch', null), emote('LULW', 'Ffz', null)];
    const ids = pickerSections(unowned, [], [], '', context).map((section) =>
      section.id);

    expect(ids).toEqual(['others-Twitch', 'others-Ffz']);
  });

  it('shows the favorites above everything, in the order given, and only the ones usable here', () => {
    const favorites = [
      favoriteKey('Bttv', 'id-OMEGALUL'),
      favoriteKey('Twitch', 'id-gone'),
      favoriteKey('Twitch', 'id-zedHi'),
    ];

    expect(layout({ ...context, favorites })[0]).toEqual(['favorites', ['OMEGALUL', 'zedHi'], undefined]);
  });

  // Stoney 2026-10-14: "right click does nothing anymore, and yes i like that favorite option for sure". An
  // emoji is starred under the Emoji source, by its glyph.
  it('lists a starred emoji among the favorites', () => {
    const grin = {
      glyph: '😀',
      shortcodes: ['grinning'],
      group: 0,
      tags: [],
      skins: [],
    };
    const favorites = [favoriteKey('Emoji', '😀'), favoriteKey('Twitch', 'id-zedHi')];
    const found = pickerSections(emotes, [grin], [], '', { ...context, favorites })[0]!;

    expect(found.id).toBe('favorites');

    expect(found.items.map((item) =>
      item.insert)).toEqual(['😀', 'zedHi']);
  });

  // Stoney 2026-10-14: "instead of removing the tabs, make them grayed out and locked and keep the remaining tabs
  // enabled, but please add the channel grouping".
  it('keeps every section while searching, each holding only its matches', () => {
    const favorites = [favoriteKey('Twitch', 'id-zedHi')];
    const found = pickerSections(emotes, [], [], 'h', { ...context, favorites }).map((section) =>
      [section.id, section.items.map((item) =>
        item.insert)]);

    expect(found).toEqual([
      ['favorites', ['zedHi']],
      ['own', []],
      ['channel', []],
      ['owner-ada', ['adaHype']],
      ['owner-zed', ['zedHi']],
      ['others-Twitch', ['ghostly']],
      ['global-Twitch', []],
      ['global-SevenTv', ['PepeLaugh']],
      ['global-Bttv', []],
      ['global-Ffz', []],
    ]);
  });

  it('puts the best match first inside a section', () => {
    const codes = [emote('xLULx', 'Ffz', 'global'), emote('LULW', 'Ffz', 'global')];
    const section = pickerSections(codes, [], [], 'lul', context)[0]!;

    expect(section.items.map((item) =>
      item.insert)).toEqual(['LULW', 'xLULx']);
  });

  // Stoney 2026-10-14: "no "other" and no "emotes" just the name in the heading collapse".
  it('names your own section after you', () => {
    const named = pickerSections([emote('myOwn', 'Twitch', 'own')], [], [], '', {
      ...context,
      ownName: 'Stoney_Eagle',
    });

    expect(named[0]).toMatchObject({ id: 'own', name: 'Stoney_Eagle' });
  });

  // Stoney 2026-10-14: "use the channel avatar instead of its first emote".
  it('carries the avatar of the channel a section belongs to', () => {
    const avatars = {
      ...context,
      channelAvatar: 'https://cdn.test/stoney.png',
      ownAvatar: 'https://cdn.test/me.png',
    };
    const withOwner = [
      { ...emote('zedHi', 'Twitch', 'other', 'Zed'), ownerAvatarUrl: 'https://cdn.test/zed.png' },
      emote('stoneyWave', 'Twitch', 'channel'),
      emote('myOwn', 'Twitch', 'own'),
      emote('Kappa', 'Twitch', 'global'),
    ];
    const found = pickerSections(withOwner, [], [], '', avatars).map((section) =>
      [section.id, section.avatar]);

    expect(found).toEqual([
      ['own', 'https://cdn.test/me.png'],
      ['channel', 'https://cdn.test/stoney.png'],
      ['owner-zed', 'https://cdn.test/zed.png'],
      ['global-Twitch', undefined],
    ]);
  });
});

// Stoney 2026-10-14: "someone needs to be able to choose a skin tone once and the picker needs to swap them to that
// skin tone for all emojis, this is very tedious".
describe('one skin tone for every emoji', () => {
  const tones = ['🏻', '🏼', '🏽', '🏾', '🏿'];
  const wave = { insert: '👋', skins: tones.map((tone) =>
    `👋${tone}`) };
  // A handshake comes in every pair of tones; only the pair of the chosen tone is that tone.
  const shake = { insert: '🤝', skins: ['🤝🏻', '🫱🏻‍🫲🏼', '🫱🏼‍🫲🏻', '🤝🏼'] };

  it('gives the yellow emoji for the default tone', () => {
    expect(skinTone(wave, 0)).toBe('👋');
  });

  it('gives the version of the chosen tone', () => {
    expect(skinTone(wave, 3)).toBe('👋🏽');
    expect(skinTone(wave, 5)).toBe('👋🏿');
  });

  it('takes the version where every person has the chosen tone', () => {
    expect(skinTone(shake, 2)).toBe('🤝🏼');
  });

  it('keeps the emoji as it is when it has no skin tones or not that one', () => {
    expect(skinTone({ insert: '🍔', skins: [] }, 4)).toBe('🍔');
    expect(skinTone(shake, 5)).toBe('🤝');
  });
});
