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

import type { CommunityMember } from '@/api/community';

import { roleKeys } from '../profile/profileRoles';

function person(flags: Partial<CommunityMember>): CommunityMember {
  return {
    id: '1',
    username: 'ada',
    displayName: 'Ada',
    messageCount: 0,
    watchHours: 0,
    commandsUsed: 0,
    trustLevel: 'viewer',
    isBanned: false,
    recentActivity: [],
    banHistory: [],
    ...flags,
  } as CommunityMember;
}

describe('roleKeys artist', () => {
  it('shows the artist role when Twitch says isArtist', () => {
    expect(roleKeys(person({ isArtist: true }), null, 'streamer')).toEqual(['artist']);
  });

  it('shows no artist role when Twitch says false or has no answer', () => {
    expect(roleKeys(person({ isArtist: false }), null, 'streamer')).toEqual([]);
    expect(roleKeys(person({ isArtist: null }), null, 'streamer')).toEqual([]);
  });
});
