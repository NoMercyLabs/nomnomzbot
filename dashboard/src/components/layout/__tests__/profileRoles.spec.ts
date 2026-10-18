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
