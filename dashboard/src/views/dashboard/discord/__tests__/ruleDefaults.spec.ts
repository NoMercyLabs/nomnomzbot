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

import type { DiscordChannel } from '@/api/discord';

import { defaultChannelFor } from '../ruleDefaults';

function channel(name: string, canPost = true, type = 0): DiscordChannel {
  return {
    id: name,
    name,
    type,
    parentId: null,
    position: 0,
    canPost,
    unavailableReasonCode: null,
    unavailableReason: null,
  };
}

describe('the channel a new Discord rule starts with', () => {
  it('picks the live-streams channel for go-live', () => {
    const channels = [channel('general'), channel('live-streams'), channel('memes')];

    expect(defaultChannelFor('go_live', channels)).toBe('live-streams');
  });

  it('prefers a channel the bot can post in over a better named one it cannot', () => {
    const channels = [channel('live-streams', false), channel('announcements')];

    expect(defaultChannelFor('go_live', channels)).toBe('announcements');
  });

  it('takes the better named channel when the bot can post in none, since it raises its own access', () => {
    const channels = [channel('memes', false), channel('live-streams', false)];

    expect(defaultChannelFor('go_live', channels)).toBe('live-streams');
  });

  it('picks an alerts channel for a follow and falls back to general', () => {
    expect(defaultChannelFor('new_clip', [channel('general'), channel('alerts')])).toBe(
      'alerts',
    );

    expect(defaultChannelFor('new_clip', [channel('support'), channel('general')])).toBe(
      'general',
    );
  });

  it('skips voice channels and categories', () => {
    const channels = [channel('Voice', true, 2), channel('Info', true, 4), channel('chat')];

    expect(defaultChannelFor('schedule', channels)).toBe('chat');
  });

  it('answers nothing when there is no text channel at all', () => {
    expect(defaultChannelFor('go_live', [channel('Voice', true, 2)])).toBe('');
  });
});
