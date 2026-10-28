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

import { rungNames, rungs } from '../permissions';
import {
  emoteProviders,
  musicProviders,
  musicRoleRungs,
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
