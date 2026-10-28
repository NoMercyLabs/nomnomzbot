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

import { describe, expect, it } from 'vitest';

import { toChatLine, type ExternalChatPayload } from '../externalChat';

const kappa = {
  provider: 'Twitch',
  id: '25',
  code: 'Kappa',
  urls: {
    1: 'https://static-cdn.jtvnw.net/emoticons/v2/25/static/dark/1.0',
  },
  animated: false,
  zeroWidth: false,
};

const payload = {
  kind: 'Message',
  broadcasterTwitchId: '424242',
  messageId: 'm-2',
  chatterTwitchId: '55',
  chatterLogin: 'someone',
  chatterDisplayName: 'Someone',
  color: null,
  text: 'hi Kappa',
  fragments: [
    {
      type: 'text',
      text: 'hi ',
      emote: null,
      cheermote: null,
      mention: null,
      linkPreview: null,
    },
    {
      type: 'emote',
      text: 'Kappa',
      emote: kappa,
      cheermote: null,
      mention: null,
      linkPreview: null,
    },
  ],
  badges: [
    {
      setId: 'subscriber',
      id: '12',
      info: '12',
      urls: {
        1: 'https://badge/offbot-sub/1',
      },
    },
  ],
  replyParentMessageId: null,
  timestamp: '2026-10-17T12:00:00Z',
} as ExternalChatPayload;

describe('toChatLine', () => {
  it('keeps the badge image URLs the server resolved for the off-bot channel', () => {
    const line = toChatLine(payload);

    expect(line.badges).toEqual([
      {
        setId: 'subscriber',
        id: '12',
        info: '12',
        urls: {
          1: 'https://badge/offbot-sub/1',
        },
      },
    ]);
  });

  it('keeps the emote image on the emote fragment', () => {
    const line = toChatLine(payload);

    expect(line.fragments[1]?.emote?.urls['1']).toBe(kappa.urls['1']);
    expect(line.fragments[0]?.emote).toBeNull();
  });
});
