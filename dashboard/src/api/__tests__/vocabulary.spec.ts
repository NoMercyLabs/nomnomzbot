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

import { rungNames, rungs } from '../permissions';
import {
  emoteProviders,
  musicProviders,
  musicRoleRungs,
  musicTrustLevels,
  platformScopeSets,
  streamPlatforms,
  trustTiers,
} from '../vocabulary';

describe('the role ladder names', () => {
  it('lists every rung once, lowest first, with no Followers rung', () => {
    expect(rungNames).toEqual([
      'Everyone',
      'Subscriber',
      'Vip',
      'Artist',
      'Moderator',
      'LeadModerator',
      'Editor',
      'Broadcaster',
    ]);
  });

  it('follows the bot levels, so a higher name never has a lower level', () => {
    const levels = rungs.map((rung) => rung.level);

    expect(levels).toEqual([...levels].sort((a, b) => a - b));
    expect(new Set(rungNames).size).toBe(rungs.length);
  });
});

describe('the platform words', () => {
  it('names the three platforms a channel streams on, Twitch first', () => {
    expect(streamPlatforms).toEqual(['twitch', 'kick', 'youtube']);
  });

  it('gives only a platform that signs in separately a scope set', () => {
    expect(platformScopeSets).toEqual({ kick: 'kick.chat', youtube: 'youtube.manage' });
    const keys: readonly string[] = Object.keys(platformScopeSets);
    const platforms: readonly string[] = streamPlatforms;

    expect(keys.every((key) => platforms.includes(key))).toBe(true);
    expect('twitch' in platformScopeSets).toBe(false);
  });
});

describe('the emote and music words', () => {
  it('lists emote providers in picker order, Twitch before the third-party sets', () => {
    expect(emoteProviders).toEqual(['Twitch', 'SevenTv', 'Bttv', 'Ffz']);
  });

  it('lets the bot pick a music provider unless one is named', () => {
    expect(musicProviders[0]).toBe('auto');
    expect(musicProviders).toEqual(['auto', 'spotify', 'youtube']);
  });

  it('ranks the song-request trust levels from everyone up to the broadcaster', () => {
    expect(musicTrustLevels[0]).toBe('everyone');
    expect(musicTrustLevels.at(-1)).toBe('broadcaster');
    expect(musicTrustLevels).toEqual(['everyone', 'subscribers', 'vip', 'moderators', 'broadcaster']);
  });

  it('ranks the per-role song caps from viewer up to the broadcaster', () => {
    expect(musicRoleRungs).toEqual(['viewer', 'subscriber', 'vip', 'moderator', 'broadcaster']);
  });
});

describe('the trust tier names', () => {
  it('runs from Untrusted to Established and holds the channel-earned Regular tier', () => {
    expect(trustTiers[0]).toBe('Untrusted');
    expect(trustTiers.at(-1)).toBe('Established');
    expect(trustTiers).toContain('Regular');
    expect(trustTiers).toHaveLength(7);
  });
});
