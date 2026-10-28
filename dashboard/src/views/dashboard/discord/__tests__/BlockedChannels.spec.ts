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

import { mount } from '@vue/test-utils';
import { beforeEach, describe, expect, it } from 'vitest';

import type { DiscordChannel } from '@/api/discord';
import { i18n } from '@/i18n';

import BlockedChannels from '../components/BlockedChannels.vue';
import en from '../i18n/en.json';

function channel(name: string, canPost: boolean): DiscordChannel {
  return {
    id: name,
    name,
    type: 0,
    parentId: null,
    position: 0,
    canPost,
    unavailableReasonCode: null,
    unavailableReason: null,
  };
}

function show(channels: DiscordChannel[]): string {
  return mount(BlockedChannels, { props: { channels }, global: { plugins: [i18n] } }).text();
}

describe('the note under a Discord channel picker', () => {
  beforeEach(() => {
    i18n.global.mergeLocaleMessage('en', en);
  });

  it('names every channel the bot cannot post in and says how to allow it', () => {
    const text = show([channel('support', true), channel('live-streams', false), channel('memes', false)]);

    expect(text).toContain('#live-streams, #memes');
    expect(text).not.toContain('#support');
    expect(text).toContain('Send Messages');
  });

  it('says nothing when the bot can post everywhere', () => {
    expect(show([channel('support', true)])).toBe('');
  });
});
