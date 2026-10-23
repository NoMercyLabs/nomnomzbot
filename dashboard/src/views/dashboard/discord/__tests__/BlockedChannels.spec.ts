// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
